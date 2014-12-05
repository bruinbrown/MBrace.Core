﻿namespace Nessos.MBrace

/// Represent a distributed atomically updatable value reference
type ICloudAtom<'T> =
    inherit ICloudDisposable

    /// Cloud atom identifier
    abstract Id : string

    /// Returns the current value of atom.
    abstract Value : 'T

    /// Asynchronously returns the current value of atom.
    abstract GetValue : unit -> Async<'T>

    /// <summary>
    ///     Atomically updates table entry of given id using updating function.
    /// </summary>
    /// <param name="updater">Value updating function</param>
    /// <param name="maxRetries">Maximum retries under optimistic semantics. Defaults to infinite.</param>
    abstract Update : updater:('T -> 'T) * ?maxRetries:int -> Async<unit>

    /// <summary>
    ///      Forces a value on atom.
    /// </summary>
    /// <param name="value">value to be set.</param>
    abstract Force : value:'T -> Async<unit>

[<AutoOpen>]
module CloudAtomUtils =
    
    type ICloudAtom<'T> with

        /// <summary>
        ///     Transactionally updates the contained value.
        /// </summary>
        /// <param name="transaction">Transaction function.</param>
        member atom.Transact(transaction : 'T -> 'R * 'T) : Async<'R> = async {
            let result = ref Unchecked.defaultof<'R>
            do! atom.Update(fun t -> let r,t' = transaction t in result := r ; t')
            return result.Value
        }

namespace Nessos.MBrace.Store
 
open Nessos.MBrace

/// Defines a factory for distributed atoms
type ICloudAtomProvider =

    /// Implementation name
    abstract Name : string

    /// Cloud atom identifier
    abstract Id : string

    /// Returns a serializable atom provider descriptor
    /// that can be used in remote processes.
    abstract GetAtomProviderDescriptor : unit -> ICloudAtomProviderDescriptor

    /// Create a uniquely specified container name.
    abstract CreateUniqueContainerName : unit -> string

    /// <summary>
    ///     Checks if provided value is supported in atom instances.
    /// </summary>
    /// <param name="value">Value to be checked.</param>
    abstract IsSupportedValue : value:'T -> bool

    /// <summary>
    ///     Creates a new atom instance with given initial value.
    /// </summary>
    /// <param name="container">Atom container.</param>
    /// <param name="initValue"></param>
    abstract CreateAtom<'T> : container:string * initValue:'T -> Async<ICloudAtom<'T>>

    /// <summary>
    ///     Disposes all atoms in provided container
    /// </summary>
    /// <param name="container">Atom container.</param>
    abstract DisposeContainer : container:string -> Async<unit>

/// Defines a serializable atom provider descriptor
/// used for recovering instances in remote processes.
and ICloudAtomProviderDescriptor =
    /// Implementation name
    abstract Name : string
    /// Descriptor Identifier
    abstract Id : string
    /// Recovers the atom provider instance locally
    abstract Recover : unit -> ICloudAtomProvider


type AtomConfiguration =
    {
        AtomProvider : ICloudAtomProvider
        DefaultContainer : string
    }

namespace Nessos.MBrace

open Nessos.MBrace.Continuation
open Nessos.MBrace.Store

#nowarn "444"

type CloudAtom =
    
    /// <summary>
    ///     Creates a new cloud atom instance with given value.
    /// </summary>
    /// <param name="initial">Initial value.</param>
    static member New<'T>(initial : 'T) : Cloud<ICloudAtom<'T>> = cloud {
        let! config = Cloud.GetResource<AtomConfiguration> ()
        return! Cloud.OfAsync <| config.AtomProvider.CreateAtom(config.DefaultContainer, initial)
    }

    /// <summary>
    ///     Dereferences a cloud atom.
    /// </summary>
    /// <param name="atom">Atom instance.</param>
    static member Read(atom : ICloudAtom<'T>) : Cloud<'T> = Cloud.OfAsync <| atom.GetValue()

    /// <summary>
    ///     Atomically updates the contained value.
    /// </summary>
    /// <param name="updater">value updating function.</param>
    /// <param name="atom">Atom instance to be updated.</param>
    static member Update (updateF : 'T -> 'T) (atom : ICloudAtom<'T>) : Cloud<unit> = 
        Cloud.OfAsync <| atom.Update updateF

    /// <summary>
    ///     Forces the contained value to provided argument.
    /// </summary>
    /// <param name="value">Value to be set.</param>
    /// <param name="atom">Atom instance to be updated.</param>
    static member Force (value : 'T) (atom : ICloudAtom<'T>) : Cloud<unit> =
        Cloud.OfAsync <| atom.Force value

    /// <summary>
    ///     Transactionally updates the contained value.
    /// </summary>
    /// <param name="trasactF"></param>
    /// <param name="atom"></param>
    static member Transact (trasactF : 'T -> 'R * 'T) (atom : ICloudAtom<'T>) : Cloud<'R> =
        Cloud.OfAsync <| atom.Transact trasactF

    /// <summary>
    ///     Deletes the provided atom instance from store.
    /// </summary>
    /// <param name="atom">Atom instance to be deleted.</param>
    static member Delete (atom : ICloudAtom<'T>) = Cloud.Dispose atom


    /// <summary>
    ///     Checks if value is supported by current table store.
    /// </summary>
    /// <param name="value">Value to be checked.</param>
    static member IsSupportedValue(value : 'T) = cloud {
        let! config = Cloud.TryGetResource<AtomConfiguration> ()
        return
            match config with
            | None -> false
            | Some ap -> ap.AtomProvider.IsSupportedValue value
    }