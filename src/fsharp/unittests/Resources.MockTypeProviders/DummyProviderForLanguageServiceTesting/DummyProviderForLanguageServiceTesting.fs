﻿namespace DummyProviderForLanguageServiceTesting 

open Microsoft.FSharp.Core.CompilerServices
open Microsoft.FSharp.TypeProvider.Emit
open System.Linq.Expressions

// Runtime methods, these are called instead of “erased” methods
type RuntimeAPI = 
    static member Convert(x:float) : decimal = decimal x
    static member AddInt(x:int) = x+1
    static member AddInt(x:int,y:int) = x+y
    static member Identity(x : string) = x
    static member IdentityInt(x : int) = x

// Linq Expression functions for erased methods calls.
module InvokeAPI = 
    let addIntX (args : Quotations.Expr list) =
        match Seq.length args with
        | 1 -> <@@ RuntimeAPI.AddInt %%args.[0] @@>
        | 2 -> <@@ RuntimeAPI.AddInt (%%args.[0], %%args.[1]) @@>
        | _ -> failwithf "addIntX: expect arity 1 or 2 (got %d)" (Seq.length args)

    let instanceX (args:Quotations.Expr list) =
        match Seq.length args with
        | 2 -> <@@ RuntimeAPI.IdentityInt (%%args.[1]) @@>
        | _ -> failwithf "instanceX: expect arity 2 (got %d)" (Seq.length args)

    let ctor (args:Quotations.Expr list) = 
        match Seq.length args with
        | 0 -> <@@ new System.Object() @@>
        | _ -> failwithf "ctor:expect arity 1 (got %d)" (Seq.length args)

module internal TPModule = 
        
    let namespaceName = "N1"    
    let thisAssembly  = System.Reflection.Assembly.GetExecutingAssembly()

    // A parametric type N1.T
    let typeT = ProvidedTypeDefinition(thisAssembly,namespaceName,"T",Some typeof<System.Object>)

    // Make an instantiation of the parametric type
    // THe instantiated type has a static property that evaluates to the param passed
    let instantiateParametricType (typeName:string) (args:System.Object[]) =
        match args with
        [| :? string as value; :? int as ignoredvalue; |] -> 
            let typeParam = ProvidedTypeDefinition(thisAssembly,namespaceName, typeName, Some typeof<System.Object>)     
            let propParam = ProvidedProperty("Param1", typeof<string>, 
                                             IsStatic = true,
                                             // A complicated was to basically return the constant value... Maybe there's a better/simpler way?
                                             GetterCode = fun _ -> <@@ RuntimeAPI.Identity(value) @@>)
            typeParam.AddMember(propParam :>System.Reflection.MemberInfo)
            typeParam 
        | _ -> failwithf "instantiateParametricType: unexpected params %A" args
    
    // N1.T<string, int>
    typeT.DefineStaticParameters( [ ProvidedStaticParameter("Param1", typeof<string>); ProvidedStaticParameter("ParamIgnored", typeof<int>) ], instantiateParametricType )

    // A non-parametric type N1.T1
    let typeT1 = ProvidedTypeDefinition(thisAssembly,namespaceName,"T1",Some typeof<System.Object>)
    
        
    // Two static methods: N1.T1.M1(int) and N1.T1.M2(int,int)
    let methM1 = ProvidedMethod("M1",[ProvidedParameter("arg1", typeof<int>)],typeof<int>, IsStaticMethod=true, InvokeCode=InvokeAPI.addIntX)
    let methM2 = ProvidedMethod("M2",[ProvidedParameter("arg1",typeof<int>);ProvidedParameter("arg2", typeof<int>)],typeof<int>, IsStaticMethod=true, InvokeCode=InvokeAPI.addIntX)

    //one Instance method:N1.T1().IM1(int)
    let methIM1 = ProvidedMethod("IM1",[ProvidedParameter("arg1", typeof<int>)],typeof<int>,IsStaticMethod=false,InvokeCode=InvokeAPI.instanceX)

    // A method involving units-of-measure
    let measures = ProvidedMeasureBuilder.Default
    let kgAnnotation = measures.SI "kilogram"    // a measure
    let hzAnnotation = measures.SI "hertz"       // a measure-abbreviation
    let kg_per_hz_squared = measures.Ratio(kgAnnotation, measures.Square hzAnnotation)
    let float_kg = measures.AnnotateType(typeof<double>,[kgAnnotation])
    let decimal_kg_per_hz_squared = measures.AnnotateType(typeof<decimal>,[kg_per_hz_squared])
    let nullable_decimal_kg_per_hz_squared = typedefof<System.Nullable<_>>.MakeGenericType [| decimal_kg_per_hz_squared |]

    let methM3 = ProvidedMethod("MethodWithTypesInvolvingUnitsOfMeasure",[ProvidedParameter("arg1", float_kg)],nullable_decimal_kg_per_hz_squared, IsStaticMethod=true, InvokeCode=(fun args -> <@@ RuntimeAPI.Convert(%%(args.[0])) @@> ))

    // an instance method using a conditional expression
    let methM4 = ProvidedMethod("MethodWithErasedCodeUsingConditional",[],typeof<int>,IsStaticMethod=false,InvokeCode=(fun _ -> <@@ if true then 1 else 2 @@>))

    // an instance method using a call and a type-as expression
    let methM5 = ProvidedMethod("MethodWithErasedCodeUsingTypeAs",[],typeof<int>,IsStaticMethod=false,InvokeCode=(fun _ -> <@@ box 1 :?> int @@>))

    // Three ctors
    let ctorA = ProvidedConstructor([],InvokeCode=InvokeAPI.ctor)
    let ctorB = ProvidedConstructor([ProvidedParameter("arg1",typeof<double>)])
    let ctorC = ProvidedConstructor([ProvidedParameter("arg1",typeof<int>); ProvidedParameter("arg2",typeof<char>)])

    typeT1.AddMember methM1
    typeT1.AddMember methM2
    typeT1.AddMember methIM1
    typeT1.AddMember methM3
    typeT1.AddMember methM4
    typeT1.AddMember methM5
    typeT1.AddMember ctorA
    typeT1.AddMember ctorB
    typeT1.AddMember ctorC

    // a nested type
    typeT1.AddMember <| ProvidedTypeDefinition("SomeNestedType", Some typeof<obj>)

    let typeWithNestedTypes = ProvidedTypeDefinition(thisAssembly,namespaceName,"TypeWithNestedTypes", Some typeof<System.Object>)
    typeWithNestedTypes.AddMember <| ProvidedTypeDefinition("X", Some typeof<obj>)
    typeWithNestedTypes.AddMember <| ProvidedTypeDefinition("Z", Some typeof<obj>)
    typeWithNestedTypes.AddMember <| ProvidedTypeDefinition("A", Some typeof<obj>)

    let types = [ typeT1 ; typeT; typeWithNestedTypes ]

// Used by unit testing to check that Dispose is being called on the type provider
module GlobalCounters = 
    let mutable creations = 0
    let mutable disposals = 0
    let GetTotalCreations() = creations
    let GetTotalDisposals() = disposals


[<TypeProvider>]
type HelloWorldProvider() = 
    inherit TypeProviderForNamespaces(TPModule.namespaceName,TPModule.types)
    do GlobalCounters.creations <- GlobalCounters.creations + 1                         
    let mutable disposed = false
    interface System.IDisposable with 
        member x.Dispose() = 
            System.Diagnostics.Debug.Assert(not disposed)
            disposed <- true
            GlobalCounters.disposals <- GlobalCounters.disposals + 1                         
            if GlobalCounters.disposals % 5 = 0 then failwith "simulate random error during disposal"


// implementation of a poorly behaving TP that sleeps for various numbers of seconds when traversing into members.
// simulates high network-latency, for testing VS robustness against badly-behaved providers.
module internal SlowIntelliSenseTPModule = 
        
    let namespaceName = "SlowIntelliSense"    
    let thisAssembly  = System.Reflection.Assembly.GetExecutingAssembly()

    let typeT = ProvidedTypeDefinition(thisAssembly,namespaceName,"T",Some typeof<System.Object>)
    let methM1 = ProvidedMethod("M1",[ProvidedParameter("arg1", typeof<int>)],typeof<int>, IsStaticMethod=true, InvokeCode=InvokeAPI.addIntX)
    typeT.AddMember methM1

    let rec populate(t:ProvidedTypeDefinition, millisDelay:int) =
        t.AddMembersDelayed(fun() ->
                System.Threading.Thread.Sleep(millisDelay)
                // it is best for the first alphabetical to be zero-delay, as we immediately try to fetch its doc tip (which includes nested members)
                let a = new ProvidedTypeDefinition("AZero", Some typeof<obj>)
                populate(a, 0)
                let e = new ProvidedTypeDefinition("Six", Some typeof<obj>)
                populate(e, 6000)
                let t = new ProvidedTypeDefinition("Three", Some typeof<obj>)
                populate(t, 3000)
                let z = new ProvidedTypeDefinition("Zero", Some typeof<obj>)
                populate(z, 0)
                [a;e;t;z]
            )

    typeT.AddMember(
            let t = new ProvidedTypeDefinition("Zero", Some typeof<obj>)
            populate(t,0)
            t
        )
    let types = [ typeT ]

[<TypeProvider>]
type SlowIntellisenseProvider() = 
    inherit TypeProviderForNamespaces(SlowIntelliSenseTPModule.namespaceName,SlowIntelliSenseTPModule.types)
    do
        ignore() // for breakpoint

[<TypeProvider>]
type ShowOffCreationTimeProvider() as this= 
    inherit TypeProviderForNamespaces()
    let namespaceName = "ShowOffCreationTime"    
    let thisAssembly  = System.Reflection.Assembly.GetExecutingAssembly()

    let typeT = ProvidedTypeDefinition(thisAssembly,namespaceName,"T",Some typeof<System.Object>)
    let timeString = "CreatedAt" + System.DateTime.Now.ToLongTimeString()
    let methM1 = ProvidedMethod(timeString,[ProvidedParameter("arg1", typeof<int>)],typeof<int>, IsStaticMethod=true, InvokeCode=InvokeAPI.addIntX)
    let types = [ typeT ]

    do
        typeT.AddMember methM1
        this.AddNamespace(namespaceName,types)


module TypeProviderThatThrowsErrorsModule = 
    type private Marker = interface end
    let assembly = typeof<Marker>.Assembly
    let rootNamespace = "TPErrors"
    let types =
        let t = ProvidedTypeDefinition(assembly, rootNamespace, "TP", Some typeof<obj>)
        let parameter = ProvidedStaticParameter("N", typeof<int>)
        t.DefineStaticParameters(
            parameters = [parameter],
            instantiationFunction = fun name args ->
                match args with
                | [|:? int as n|] when n > 0 ->
                    let errors = Seq.init n (sprintf "Error %d" >> Failure)
                    raise (System.AggregateException(errors))
                | _ -> failwith "nonexpected"
            )
        [t]

[<TypeProvider>]
type TypeProviderThatThrowsErrors() = 
    inherit TypeProviderForNamespaces(TypeProviderThatThrowsErrorsModule.rootNamespace, TypeProviderThatThrowsErrorsModule.types)

module TypeProviderForTestingTuplesErasureModule = 
    type private Marker = interface end
    let assembly = typeof<Marker>.Assembly
    
    let rootNamespace = "TupleErasure"

    let handle f = 
        function
        | [arg] -> f arg
        | _ -> failwith "One argument expected"

    let erasedTup = ProvidedTypeDefinition(assembly, rootNamespace, "TupleType", Some(typeof<int*string>))
    erasedTup.AddMember(ProvidedConstructor([ProvidedParameter("tup", typeof<int * string>)], InvokeCode = handle (fun tup -> tup)))
    erasedTup.AddMember(ProvidedProperty("SecondComponent", typeof<string>, GetterCode = handle (fun tup -> Quotations.Expr.TupleGet(tup, 1))))
    
    let objT = typedefof<int * string>.MakeGenericType(typeof<int>, erasedTup)
    let erasedCompoundTup = ProvidedTypeDefinition(assembly, rootNamespace, "CompoundTupleType", Some(objT))
    erasedCompoundTup.AddMember(ProvidedConstructor([ProvidedParameter("tup", objT)], InvokeCode = handle (fun tup -> tup)))
    erasedCompoundTup.AddMember(ProvidedProperty("First", typeof<int>, GetterCode = handle (fun tup -> Quotations.Expr.TupleGet(tup, 0))))
    erasedCompoundTup.AddMember(ProvidedProperty("Second", erasedTup, GetterCode = handle (fun tup -> Quotations.Expr.TupleGet(tup, 1))))

[<TypeProvider>]
type TypeProviderForTestingTuplesErasure() = 
    inherit TypeProviderForNamespaces(TypeProviderForTestingTuplesErasureModule.rootNamespace, [TypeProviderForTestingTuplesErasureModule.erasedTup; TypeProviderForTestingTuplesErasureModule.erasedCompoundTup])

module TypeProviderThatEmitsBadMethodsModule = 
    let assembly = System.Reflection.Assembly.GetExecutingAssembly()
    let rootNamespace = "BadMethods"
    let arrayUser = ProvidedTypeDefinition(assembly, rootNamespace, "Arr", None)
    let get = typeof<int[]>.GetMethod("Get")
    let set = typeof<int[]>.GetMethod("Set")
    let addr = typeof<int[]>.GetMethod("Address")
    arrayUser.AddMember(
        ProvidedMethod(
            methodName = "GetFirstElement", 
            parameters = [ProvidedParameter("array", typeof<int[]>)], 
            returnType = typeof<int>, 
            IsStaticMethod = true, 
            InvokeCode = function [arr] -> Quotations.Expr.Call(arr, get, [Quotations.Expr.Value 0]) | _ -> failwith "One argument expected")
        )
    arrayUser.AddMember(
        ProvidedMethod(
            methodName = "SetFirstElement", 
            parameters = [ProvidedParameter("array", typeof<int[]>); ProvidedParameter("val", typeof<int>)], 
            returnType = typeof<unit>, 
            IsStaticMethod = true, 
            InvokeCode = function [arr; v] -> Quotations.Expr.Call(arr, set, [Quotations.Expr.Value 0; v]) | _ -> failwith "Two argument expected")
        )
    arrayUser.AddMember(
        ProvidedMethod(
            methodName = "AddressOfFirstElement", 
            parameters = [ProvidedParameter("array", typeof<int[]>)], 
            returnType = typeof<int>.MakeByRefType(), 
            IsStaticMethod = true, 
            InvokeCode = function [arr] -> Quotations.Expr.Call(arr, addr, [Quotations.Expr.Value 0]) | _ -> failwith "One argument expected")
        )

[<TypeProvider>]
type TypeProviderThatEmitsBadMethods() = 
    inherit TypeProviderForNamespaces(TypeProviderThatEmitsBadMethodsModule.rootNamespace, [TypeProviderThatEmitsBadMethodsModule.arrayUser])

module TypeProvidersVisibilityChecks = 
    let assembly = System.Reflection.Assembly.GetExecutingAssembly()

    let Namespace = "GeneratedType"
    let setMethodVisibility (m : ProvidedMethod) visibility = m.SetMethodAttrs((m.Attributes &&& ~~~System.Reflection.MethodAttributes.MemberAccessMask) ||| visibility)
    let addMethod name value visibility (ty : ProvidedTypeDefinition) = 
        let m = ProvidedMethod(name, [], value.GetType())
        m.IsStaticMethod <- false
        m.InvokeCode <- fun _ -> Quotations.Expr.Value(value, value.GetType())
        setMethodVisibility m visibility
        ty.AddMember m

    let addGetProperty name value visibility (ty : ProvidedTypeDefinition) = 
        let prop = ProvidedProperty(name, value.GetType())
        ty.AddMember prop
        prop.IsStatic <- false
        prop.GetterCode <- fun _ -> Quotations.Expr.Value(value, value.GetType())
        let m = prop.GetGetMethod() :?> ProvidedMethod
        setMethodVisibility m visibility

    let addLiteralField name value (ty : ProvidedTypeDefinition) = 
        let f = ProvidedLiteralField(name, value.GetType(), value)
        ty.AddMember f

    let providedTy = 
        let ty = ProvidedTypeDefinition(assembly, Namespace, "SampleType", Some typeof<obj>, IsErased=false)

        /// unseal type
        ty.SetAttributes(ty.Attributes &&& ~~~System.Reflection.TypeAttributes.Sealed)

        // add public literal field
        addLiteralField "PublicField" 100  ty
        
        // implicitly adds field
        let ctor = ProvidedConstructor([ProvidedParameter("f", typeof<int>)])
        ctor.InvokeCode <- fun _ -> <@@ () @@>
        ty.AddMember ctor

        // add properties
        addGetProperty "PublicProp" 10 (System.Reflection.MethodAttributes.Public) ty
        addGetProperty "ProtectedProp" 210 (System.Reflection.MethodAttributes.Family) ty
        addGetProperty "PrivateProp" 310 (System.Reflection.MethodAttributes.Private) ty

        // add methods
        addMethod "PublicM" 510 (System.Reflection.MethodAttributes.Public) ty
        addMethod "ProtectedM" 5210 (System.Reflection.MethodAttributes.Family) ty
        addMethod "PrivateM" 5310 (System.Reflection.MethodAttributes.Private) ty
        
        ty.ConvertToGenerated(System.IO.Path.GetTempFileName() + ".dll")
        ty

    [<TypeProvider>]
    type TypeProvider() = 
        inherit TypeProviderForNamespaces(Namespace, [providedTy])

module RegexTypeProvider =

    open System.Text.RegularExpressions

    [<TypeProvider>]
    type public CheckedRegexProvider() as this =
        inherit TypeProviderForNamespaces()

        // Get the assembly and namespace used to house the provided types
        let thisAssembly = System.Reflection.Assembly.GetExecutingAssembly()
        let rootNamespace = "Samples.FSharp.RegexTypeProvider"
        let baseTy = typeof<obj>
        let staticParams = [ProvidedStaticParameter("pattern", typeof<string>)]

        let regexTy = ProvidedTypeDefinition(thisAssembly, rootNamespace, "RegexTyped", Some baseTy)

        do regexTy.DefineStaticParameters(
            parameters=staticParams, 
            instantiationFunction=(fun typeName parameterValues ->

              match parameterValues with 
              | [| :? string as pattern|] -> 
                // Create an instance of the regular expression. 
                //
                // This will fail with System.ArgumentException if the regular expression is invalid. 
                // The exception will excape the type provider and be reported in client code.
                let r = System.Text.RegularExpressions.Regex(pattern)            

                // Declare the typed regex provided type.
                // The type erasure of this typs ia 'obj', even though the representation will always be a Regex
                // This, combined with hiding the object methods, makes the IntelliSense experience simpler.
                let ty = ProvidedTypeDefinition(
                            thisAssembly, 
                            rootNamespace, 
                            typeName, 
                            baseType = Some baseTy)

                ty.AddXmlDoc "A strongly typed interface to the regular expression '%s'"

                // Provide strongly typed version of Regex.IsMatch static method
                let isMatch = ProvidedMethod(
                                methodName = "IsMatch", 
                                parameters = [ProvidedParameter("input", typeof<string>)], 
                                returnType = typeof<bool>, 
                                IsStaticMethod = true,
                                InvokeCode = fun args -> <@@ Regex.IsMatch(%%args.[0], pattern) @@>) 

                isMatch.AddXmlDoc "Indicates whether the regular expression finds a match in the specified input string"

                ty.AddMember isMatch

                // Provided type for matches
                // Again, erase to obj even though the representation will always be a Match
                let matchTy = ProvidedTypeDefinition(
                                "MatchType", 
                                baseType = Some baseTy, 
                                HideObjectMethods = true)

                // Nest the match type within parameterized Regex type
                ty.AddMember matchTy
        
                // Add group properties to match type
                for group in r.GetGroupNames() do
                    // ignore the group named 0, which represents all input
                    if group <> "0" then
                        let prop = ProvidedProperty(
                                    propertyName = group, 
                                    propertyType = typeof<Group>, 
                                    GetterCode = fun args -> <@@ ((%%args.[0]:obj) :?> Match).Groups.[group] @@>)
                        prop.AddXmlDoc(sprintf @"Gets the ""%s"" group from this match" group)
                        matchTy.AddMember(prop)

                // Provide strongly typed version of Regex.Match instance method
                let matchMeth = ProvidedMethod(
                                    methodName = "Match", 
                                    parameters = [ProvidedParameter("input", typeof<string>)], 
                                    returnType = matchTy, 
                                    InvokeCode = fun args -> <@@ ((%%args.[0]:obj) :?> Regex).Match(%%args.[1]) :> obj @@>)
                matchMeth.AddXmlDoc "Searches the specified input string for the first occurence of this regular expression"
            
                ty.AddMember matchMeth
            
                // Declare a constructor
                let ctor = ProvidedConstructor(
                            parameters = [], 
                            InvokeCode = fun args -> <@@ Regex(pattern) :> obj @@>)

                // Add documentation to the constructor
                ctor.AddXmlDoc "Initializes a regular expression instance"

                ty.AddMember ctor
            
                ty
              | _ -> failwith "unexpected parameter values")) 

        do this.AddNamespace(rootNamespace, [regexTy])

[<assembly:TypeProviderAssembly>]
do()
