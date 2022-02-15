﻿module FSharp.Explicit.Json.ParserTests

open Expecto
open Expecto.Flip.Expect
open System.Text.Json
open FsToolkit.ErrorHandling
open FSharp.Explicit.Json.Parse

[<AutoOpen>]
module rec TestTypes =
    type Name = string
    type Scalar = { name: Name; value: decimal }
    type Vector = { name: Name; value: decimal list }
    type Operation = { name: string; valueA: Value; valueB: Value }

    type Formula =
    | Add of Operation
    | Subtract of Operation

    type Value =
    | None
    | Reference of Name
    | Scalar of Scalar
    | Vector of Vector
    | Formula of Formula

let leftError (path: string list) (reason: JsonParserErrorReason<'t>) =
    [{ path = path; reason = reason }] |> Error

let error (path: string list) (reason: JsonParserErrorReason<'t>) =
    { path = path; reason = reason }

let parse (json: string) f =
    let doc = JsonDocument.Parse(json)
    Parse.document f doc

let tests =
    testList "Parser Tests" [
        
        testCase "Sample" <| fun _ ->

            let json =
                """{
                    "type": "Formula",
                    "name": "Test",
                    "valueA": "Value1",
                    "valueB": {
                        "type": "Vector",
                        "value": [1, 2, 3]
                    }
                }"""

            let doc = JsonDocument.Parse(json)

            let rec parseValue (node: ParserContext) = validation {
                match node.NodeType with
                | Null ->
                    return None
                | String ->
                    let! name = Parse.string node
                    return Reference name
                | Object ->
                    let! typeName = node.prop "type" Parse.string

                    return! validation {
                        let! name = node.prop "name" Parse.string
                        match typeName with
                        | "Scalar" ->
                            let! scalar = Parse.decimal node
                            return Scalar { name = name; value = scalar}
                        | "Vector" ->
                            let! vector = Parse.list Parse.decimal node
                            return Vector { name = name; value = vector}
                        | "Add"
                        | "Subtract" ->
                            let! typeCreator =
                                match typeName with
                                | "Add" -> Ok Add
                                | "Subtract" -> Ok Subtract
                                | _ -> UserError "Unexpected formula type" |> Parse.liftError node
                            let! valueA = node.prop "valueA" parseValue
                            and! valueB = node.prop "valueB" parseValue
                            return Formula (typeCreator {
                                name = name
                                valueA = valueA
                                valueB = valueB
                            })
                        | _ ->
                            return! UserError "Unexpected type when matching on value"  |> Parse.liftError node
                    }
                | x ->
                    return! UnexpectedType (Expected Object, Actual x) |> Parse.liftError node
            }

            let result = doc |> Parse.document parseValue

            printfn "%A" result
            ()

        testList "Parse Primitives" [
        
            testCase "Parse unit" <| fun _ ->
                let expected = Ok ()
                let actual = parse "null" Parse.unit
                equal "null -> Ok ()" expected actual

            testCase "Parse bool" <| fun _ ->
                let expected = Ok true
                let actual = parse "true" Parse.bool
                equal "true -> Ok true" expected actual

            testCase "Parse byte" <| fun _ ->
                let expected = Ok 256
                let actual = parse "256" Parse.int
                equal "256 -> Ok 256" expected actual

            testCase "Parse int" <| fun _ ->
                let expected = Ok 2147483647
                let actual = parse "2147483647" Parse.int
                equal "2147483647 -> Ok 2147483647" expected actual

            testCase "Parse out of range int" <| fun _ ->
                let json = "2147483648"
                let expected = ValueOutOfRange (typeof<int32>, json) |> leftError []
                let actual = parse json Parse.int
                equal (sprintf "%A -> %A" json expected) expected actual

            testCase "Parse decimal" <| fun _ ->
                let expected = Ok 0.1111111111111111111111111111m
                let json = "0.1111111111111111111111111111"
                let actual = parse json Parse.decimal
                equal (sprintf "%A -> %A" json expected) expected actual

            // Parse Numeric

            // Parse DateTimeOffset/DateTime/Date

            // Parse TimeSpan

            // Parse Enum

            testCase "Parse list" <| fun _ ->
                let expected = Ok [ 1; 2; 3 ]
                let actual = parse "[1, 2, 3]" (Parse.list Parse.int)
                equal "[1, 2, 3] -> Ok [1; 2; 3]" expected actual
        ]

        // Parse Option

        testList "Parse Tuples" [
            testCase "Parse Tuple2" <| fun _ ->
                let expected = Ok(1, "a")

                let json = """[1, "a"]"""
                let actual = parse json (Parse.tuple2 Parse.int Parse.string)

                equal $"""{json} -> (1, "a")""" expected actual

            testCase "Parse Tuple2 types mismatch" <| fun _ ->
                let expected =
                    Error [
                        UnexpectedType (Expected Number, Actual Bool) |> error []
                        UnexpectedType (Expected String, Actual Number) |> error []
                    ]

                let json = """[false, 1]"""
                let actual = parse json (Parse.tuple2 Parse.int Parse.string)

                equal $"""{json} -> Type Error for (int * string)""" expected actual

            testCase "Parse Tuple3" <| fun _ ->
                let expected = Ok(1, "a", false)

                let json = """[1, "a", false]"""
                let actual = parse json (Parse.tuple3 Parse.int Parse.string Parse.bool)

                equal $"""{json} -> (1, "a", false)""" expected actual
        ]

        testList "Parse Error Handling" [
            testCase "Missing Properties" <| fun _ ->
        
                let json = "{}"
        
                let doc = JsonDocument.Parse(json)
                let actual = doc |> Parse.document (fun node -> validation {
                    let! typeName = node.prop "type" Parse.string
                    and! prop1 = node.prop "prop1" Parse.string
                    and! prop2 = node.prop "prop2" Parse.string
                    ()
                })
        
                let expected =
                    Error [
                        { path = []; reason = MissingProperty "type"}
                        { path = []; reason = MissingProperty "prop1"}
                        { path = []; reason = MissingProperty "prop2"}
                    ]

                equal $"""All three properties are missing""" expected actual
        ]

        // Parse Discriminated Union

        // Parse Choice/Union/OneOf

        // Parse Auto Object
    ]
