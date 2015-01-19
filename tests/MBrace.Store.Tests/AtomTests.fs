﻿namespace MBrace.Store.Tests

open System

open MBrace
open MBrace.Store
open MBrace.Continuation

open Nessos.FsPickler

open NUnit.Framework
open FsUnit

[<AutoOpen>]
module private Helpers =
    [<Literal>]
#if DEBUG
    let repeats = 10
#else
    let repeats = 3
#endif

[<TestFixture; AbstractClass>]
type ``Atom Tests`` (atomProvider : ICloudAtomProvider, ?npar, ?nseq) =

    let testContainer = atomProvider.CreateUniqueContainerName()

    let run x = Async.RunSync x
    let runLocal x = Cloud.ToAsync(x, resources = resource { yield Runtime.InMemory.ThreadPoolRuntime.Create() :> IRuntimeProvider }) |> run

    let npar = defaultArg npar 20
    let nseq = defaultArg nseq 20

    [<TestFixtureTearDown>]
    member __.TearDown() =
        atomProvider.DisposeContainer testContainer |> run

    [<Test>]
    member __.``UUID is not null or empty.`` () = 
        String.IsNullOrEmpty atomProvider.Id
        |> should equal false

    [<Test>]
    member __.``Atom provider should be serializable`` () =
        let atomProvider' = FsPickler.Clone atomProvider
        atomProvider'.Name |> should equal atomProvider.Name
        atomProvider'.Id |> should equal atomProvider.Id

    [<Test>]
    member __.``Create, dereference and delete`` () =
        let value = ("key",42)
        let atom = atomProvider.CreateAtom(testContainer, value) |> run
        atom.Value |> runLocal |> should equal value
        atom |> Cloud.Dispose |> runLocal
        shouldfail (fun () -> atom.Value |> ignore)

    [<Test>]
    member __.``Create and dispose container`` () =
        let container = atomProvider.CreateUniqueContainerName()
        let atom = atomProvider.CreateAtom(container, 42) |> run
        atom.Value |> runLocal |> should equal 42
        atomProvider.DisposeContainer container |> run
        shouldfail (fun () -> atom.Value |> ignore)

    [<Test>]
    member __.``Update sequentially`` () =
        let atom = atomProvider.CreateAtom(testContainer,0) |> run
        for i = 1 to 10 * nseq do 
            atom.Update(fun i -> i + 1) |> runLocal

        atom.Value |> runLocal |> should equal (10 * nseq)

    [<Test; Repeat(repeats)>]
    member __.``Update with contention -- int`` () =
        let atom = atomProvider.CreateAtom(testContainer, 0) |> run
        let worker _ = cloud {
            for i in 1 .. nseq do
                do! atom.Update(fun i -> i + 1)
        }

        Array.init npar worker |> Cloud.Parallel |> Cloud.Ignore |> runLocal
        atom.Value |> runLocal |> should equal (npar * nseq)

    [<Test; Repeat(repeats)>]
    member __.``Update with contention -- list`` () =
        if atomProvider.IsSupportedValue [1..100] then
            let atom = atomProvider.CreateAtom<int list>(testContainer, []) |> run
            let worker _ = cloud {
                for i in 1 .. nseq do
                    do! atom.Update(fun xs -> i :: xs)
            }

            Array.init npar worker |> Cloud.Parallel |> Cloud.Ignore |> runLocal
            atom.Value |> runLocal |> List.length |> should equal (npar * nseq)

    [<Test; Repeat(repeats)>]
    member __.``Force value`` () =
        let npar = npar
        let atom = atomProvider.CreateAtom<int>(testContainer, 0) |> run

        let worker i = cloud {
            if i = npar / 2 then
                do! atom.Force 42
            else
                do! atom.Update id
        }

        Array.init npar worker |> Cloud.Parallel |> Cloud.Ignore |> runLocal
        atom.Value |> runLocal |> should equal 42