﻿namespace MBrace

open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Text

/// Replacement implementation for System.IO.StreamReader which
/// will not expose number of bytes read in underlying stream.
/// This is used by RangedLineReader for proper partitioning of large text files.
type  StreamLineReader(stream : Stream, ?encoding : Encoding) = 
    let reader = match encoding with None -> new StreamReader(stream) | Some e -> new StreamReader(stream, e)
    let buffer : char [] = Array.zeroCreate 4096
    let mutable posInBuffer : int = -1
    let mutable numOfChars : int = 0
    let mutable endOfStream = false
    let mutable numberOfBytesRead = 0L
    let stringBuilder = new StringBuilder()
    /// Reads a line of characters from the current stream and returns the data as a string.
    member self.ReadLine() : string = 
        if endOfStream then 
            null
        else
            let mutable lineEndFlag = false
            while not lineEndFlag && not endOfStream do
                if posInBuffer = -1 then
                    posInBuffer <- 0
                    numOfChars <- reader.ReadBlock(buffer, posInBuffer, buffer.Length)
                    if numOfChars = 0 then
                        endOfStream <- true
                if not endOfStream then
                    let mutable i = posInBuffer 
                    while not lineEndFlag && i < numOfChars do
                        if buffer.[i] = '\n' then
                            stringBuilder.Append(buffer, posInBuffer, i - posInBuffer) |> ignore
                            lineEndFlag <- true
                            posInBuffer <- i + 1
                            numberOfBytesRead <- numberOfBytesRead + 1L
                        elif buffer.[i] = '\r' then
                            if i + 1 < numOfChars then
                                if buffer.[i + 1] = '\n' then
                                    stringBuilder.Append(buffer, posInBuffer, i - posInBuffer) |> ignore
                                    lineEndFlag <- true
                                    posInBuffer <- i + 2
                                    numberOfBytesRead <- numberOfBytesRead + 2L
                                else
                                    stringBuilder.Append(buffer, posInBuffer, i - posInBuffer) |> ignore
                                    lineEndFlag <- true
                                    posInBuffer <- i + 1
                                    numberOfBytesRead <- numberOfBytesRead + 1L
                            else 
                                let currentChar = char <| reader.Read()
                                if currentChar = '\n' then
                                    stringBuilder.Append(buffer, posInBuffer, i - posInBuffer) |> ignore
                                    lineEndFlag <- true
                                    posInBuffer <- -1
                                    numberOfBytesRead <- numberOfBytesRead + 2L
                                else
                                    stringBuilder.Append(buffer, posInBuffer, i - posInBuffer) |> ignore
                                    lineEndFlag <- true
                                    buffer.[0] <- currentChar
                                    posInBuffer <- -1
                                    numberOfBytesRead <- numberOfBytesRead + 1L
                        i <- i + 1
                
                    if not lineEndFlag then
                        stringBuilder.Append(buffer, posInBuffer, numOfChars - posInBuffer) |> ignore
                    if i = numOfChars then
                        posInBuffer <- -1
            
            let result = stringBuilder.ToString()
            stringBuilder.Clear() |> ignore
            numberOfBytesRead <- numberOfBytesRead + (int64 <| reader.CurrentEncoding.GetByteCount(result))
            result 

    /// The total number of bytes read
    member self.BytesRead = numberOfBytesRead

type private StreamLineEnumerator(stream : Stream, ?encoding : Encoding) =
    let mutable currentLine = Unchecked.defaultof<string>
    let reader = 
        match encoding with 
        | None -> new StreamReader(stream) 
        | Some e -> new StreamReader(stream, e)

    interface IEnumerator<string> with
        member __.Current = currentLine
        member __.Current = box currentLine
        member __.MoveNext () =
            match reader.ReadLine () with
            | null -> false
            | line -> currentLine <- line ; true

        member __.Dispose () = stream.Dispose()
        member __.Reset () = raise <| new NotSupportedException("LineReader")

type private RangedStreamLineEnumerator (stream : Stream, beginPos : int64, endPos : int64, ?encoding : Encoding) =
    let mutable currentLine = Unchecked.defaultof<string>
    do 
        if beginPos > endPos || endPos > stream.Length then raise <| new ArgumentOutOfRangeException("endPos")
        ignore <| stream.Seek(beginPos, SeekOrigin.Begin)

    let reader = new StreamLineReader(stream, ?encoding = encoding)

    let rec readNext () =
        let bytesRead = reader.BytesRead
        if beginPos + bytesRead <= endPos then
            let line = reader.ReadLine()
            // include line if:
            //   1. is the first line of the starting segment of a stream.
            //   2. is any successive line that fits within the stream boundary.
            if beginPos = 0L || bytesRead > 0L then
                currentLine <- line
                true
            else
                readNext()
        else
            false

    interface IEnumerator<string> with
        member __.Current = currentLine
        member __.Current = box currentLine
        member __.MoveNext () = readNext ()
        member __.Dispose () = stream.Dispose()
        member __.Reset () = raise <| new NotSupportedException("StreamLineReader")

type private StreamLineEnumerable(stream : Stream, ?encoding : Encoding) =
    interface IEnumerable<string> with
        member __.GetEnumerator() = new StreamLineEnumerator(stream, ?encoding = encoding) :> IEnumerator<string>
        member __.GetEnumerator() = new StreamLineEnumerator(stream, ?encoding = encoding) :> IEnumerator

/// Provides an enumerable implementation that reads text lines within the supplied seek range.
type private RangedStreamLineEnumerable(stream : Stream, beginPos : int64, endPos : int64, ?encoding : Encoding) =
    interface IEnumerable<string> with
        member __.GetEnumerator() = new RangedStreamLineEnumerator(stream, beginPos, endPos, ?encoding = encoding) :> IEnumerator<string>
        member __.GetEnumerator() = new RangedStreamLineEnumerator(stream, beginPos, endPos, ?encoding = encoding) :> IEnumerator

type internal TextReaders =
    
    /// <summary>
    ///     Returns an enumeration of all text lines contained in stream.
    /// </summary>
    /// <param name="stream">Reader stream.</param>
    /// <param name="encoding">Optional encoding for stream.</param>
    static member ReadLines (stream : Stream, ?encoding : Encoding) : seq<string> = 
        new StreamLineEnumerable(stream, ?encoding = encoding) :> _

    /// <summary>
    ///     Returns an enumeration of all text lines contained in stream within a supplied byte range.
    /// </summary>
    /// <param name="stream">Reader stream.</param>
    /// <param name="beginPos">Start position for stream.</param>
    /// <param name="endPos">End partition for stream.</param>
    /// <param name="encoding">Optional encoding for stream.</param>
    static member ReadLinesRanged (stream : Stream, beginPos : int64, endPos : int64, ?encoding : Encoding) : seq<string> =
        new RangedStreamLineEnumerable(stream, beginPos, endPos, ?encoding = encoding) :> _