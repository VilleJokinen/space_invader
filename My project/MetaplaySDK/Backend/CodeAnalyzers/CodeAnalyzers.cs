using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace Metaplay.CodeAnalyzers
{
    public static class Util
    {
        public static IEnumerable<ITypeSymbol> Ancestors(ITypeSymbol type)
        {
            while (type != null)
            {
                yield return type;
                type = type.BaseType;
            }
        }

        public static bool IsOrImplementsInterface(ITypeSymbol type, string interfaceFullName)
        {
            return type.ToString() == interfaceFullName
                || type.AllInterfaces.Any(iFace => iFace.ToString() == interfaceFullName);
        }

        public static bool IsDerivedFrom(ITypeSymbol type, string baseName)
        {
            return Ancestors(type).Any(ancestor => ancestor.ToString() == baseName);
        }

        public static string KludgeRemoveNewlines(string str)
        {
            return str.Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class MiscStringAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor StringInterpolationRule = new DiagnosticDescriptor(
            id:             "MP_STR_00",
            title:          "Default-formatted interpolated string",
            messageFormat:  "Content '{0}' (of type '{1}') is inside an interpolated string that gets formatted using the current culture. Consider using FormattableString.Invariant.",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly DiagnosticDescriptor StringAdditionRule = new DiagnosticDescriptor(
            id:             "MP_STR_01",
            title:          "Default-formatted object-to-string addition",
            messageFormat:  "Operand '{0}' (of type '{1}') is added to a string and possibly stringified using the current culture. Consider stringifying the operand explicitly.",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly DiagnosticDescriptor ToStringRule = new DiagnosticDescriptor(
            id:             "MP_STR_02",
            title:          "Default-formatted ToString()",
            messageFormat:  "Invocation '{0}' calls .ToString() via type '{1}', possibly using the current culture. Consider using .ToString(IFormatProvider? provider), or Metaplay.Core.Util.ObjectToStringInvariant(object) if the type is not statically IFormattable.",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly DiagnosticDescriptor GeneralDefaultFormattedInvocationRule = new DiagnosticDescriptor(
            id:             "MP_STR_03",
            title:          "Default-formatted method invocation",
            messageFormat:  "Invocation '{0}' involves type '{1}' which possibly gets stringified using the current culture. Consider stringifying the argument explicitly, or using an alternative IFormatProvider-taking method if available.",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        private static readonly DiagnosticDescriptor StringOrderRule = new DiagnosticDescriptor(
            id:             "MP_STR_04",
            title:          "Default-comparing string ordering",
            messageFormat:  "{0} '{1}' involves string ordering but no StringComparer is specified, and the comparison depends on the current culture. Consider explicitly specifying a StringComparer.",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            StringInterpolationRule,
            StringAdditionRule,
            ToStringRule,
            GeneralDefaultFormattedInvocationRule,
            StringOrderRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeInterpolatedStringExpression, SyntaxKind.InterpolatedStringExpression);
            context.RegisterSyntaxNodeAction(AnalyzeAddExpression, SyntaxKind.AddExpression, SyntaxKind.AddAssignmentExpression);
            context.RegisterSyntaxNodeAction(AnalyzeInvocationOrObjectCreationExpression, SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression);
        }

        static void AnalyzeInterpolatedStringExpression(SyntaxNodeAnalysisContext context)
        {
            InterpolatedStringExpressionSyntax interpolatedStringExpression = (InterpolatedStringExpressionSyntax)context.Node;

            // If the interpolated string gets treated as (e.g. by being assigned to) a FormattableString, it's OK.
            // Other analyzers (like CA1305) shall take it from here.
            if (context.SemanticModel.GetTypeInfo(interpolatedStringExpression).ConvertedType.ToString() == "System.FormattableString")
                return;

            bool checkedBenignContext = false;

            // Report all InterpolationSyntax contents (i.e. the stuff in the curly braces)
            // that we suspect might get culture-dependently formatted.
            foreach (InterpolatedStringContentSyntax content in interpolatedStringExpression.Contents)
            {
                if (!(content is InterpolationSyntax interpolation))
                    continue;

                ExpressionSyntax    expression      = interpolation.Expression;
                ITypeSymbol         expressionType  = context.SemanticModel.GetTypeInfo(expression).Type;

                // Skip benign types that we're reasonably sure won't cause problems
                if (IsBenignStringifiableType(expressionType))
                    continue;

                // \note Context benignness is checked lazily on first non-benign-typed expression
                if (!checkedBenignContext)
                {
                    checkedBenignContext = true;
                    // Ignore certain benign contexts such as arguments to exception constructors, we assume their formatting is not super important.
                    if (IsInBenignFormattingContext(interpolatedStringExpression, context.SemanticModel))
                        return;
                }

                context.ReportDiagnostic(Diagnostic.Create(StringInterpolationRule, interpolation.GetLocation(), interpolation, expressionType));
            }
        }

        static void AnalyzeAddExpression(SyntaxNodeAnalysisContext context)
        {
            // \note This same method handles both SyntaxKind.AddExpression and SyntaxKind.AddAssignmentExpression,
            //       i.e. x + y and x += y

            SyntaxNode      addExpression   = context.Node;
            IMethodSymbol   method          = (IMethodSymbol)context.SemanticModel.GetSymbolInfo(addExpression).Symbol;

            if (method.ContainingType.ToString() != "string")
                return;

            int nonStringIndex = method.Parameters[0].Type.ToString() != "string" ? 0
                               : method.Parameters[1].Type.ToString() != "string" ? 1
                               : -1;

            if (nonStringIndex < 0)
                return;

            BinaryExpressionSyntax      addBinaryExpression     = addExpression as BinaryExpressionSyntax;
            AssignmentExpressionSyntax  addAssignmentExpression = addExpression as AssignmentExpressionSyntax;
            ExpressionSyntax            leftOperand             = addBinaryExpression?.Left  ?? addAssignmentExpression?.Left;
            ExpressionSyntax            rightOperand            = addBinaryExpression?.Right ?? addAssignmentExpression?.Right;
            ExpressionSyntax            nonStringOperand        = nonStringIndex == 0 ? leftOperand : rightOperand;
            ITypeSymbol                 nonStringType           = context.SemanticModel.GetTypeInfo(nonStringOperand).Type;

            if (IsBenignStringifiableType(nonStringType))
                return;

            if (IsInBenignFormattingContext(addExpression, context.SemanticModel))
                return;

            context.ReportDiagnostic(Diagnostic.Create(StringAdditionRule, nonStringOperand.GetLocation(), nonStringOperand, nonStringType));
        }

        static void AnalyzeInvocationOrObjectCreationExpression(SyntaxNodeAnalysisContext context)
        {
            SyntaxNode      invocationOrCreation    = context.Node;
            IMethodSymbol   method                  = context.SemanticModel.GetSymbolInfo(invocationOrCreation).Symbol as IMethodSymbol;

            if (method == null)
                return;

            string methodName           = method.Name;
            string containingTypeStr    = method.ContainingType.ToString();

            if (methodName == "ToString" && method.Parameters.IsEmpty)
                AnalyzeToStringInvocation((InvocationExpressionSyntax)invocationOrCreation, method, context);
            else if (methodName == "Join" && containingTypeStr == "string")
                AnalyzeStringJoinLikeInvocation((InvocationExpressionSyntax)invocationOrCreation, 1, method, context);
            else if (methodName == "Concat" && containingTypeStr == "string")
                AnalyzeStringJoinLikeInvocation((InvocationExpressionSyntax)invocationOrCreation, 0, method, context);
            else if (containingTypeStr == "System.Text.StringBuilder")
            {
                if (methodName == "Append" || methodName == "Insert")
                    AnalyzeStringBuilderAppendOrInsertInvocation((InvocationExpressionSyntax)invocationOrCreation, method, context);
                else if (methodName == "AppendJoin")
                    AnalyzeStringJoinLikeInvocation((InvocationExpressionSyntax)invocationOrCreation, 1, method, context);
            }
            else if (containingTypeStr == "System.Linq.Enumerable"
                     && (methodName == "OrderBy"
                      || methodName == "OrderByDescending"
                      || methodName == "ThenBy"
                      || methodName == "ThenByDescending")
                     && method.TypeArguments[1].ToString() == "string")
            {
                AnalyzeStringOrdering(invocationOrCreation, method, context);
            }
            else if (containingTypeStr == "System.Collections.Generic.List<string>" && methodName == "Sort")
                AnalyzeStringOrdering(invocationOrCreation, method, context);
            else if (containingTypeStr == "System.Array" && methodName == "Sort" && method.TypeArguments.Any() && method.TypeArguments[0].ToString() == "string")
                AnalyzeStringOrdering(invocationOrCreation, method, context);
            else if (containingTypeStr == "System.Collections.Generic.SortedSet<string>" && methodName == ".ctor")
                AnalyzeStringOrdering(invocationOrCreation, method, context);
            else if (containingTypeStr.StartsWith("System.Collections.Generic.SortedDictionary<string,", StringComparison.Ordinal) && methodName == ".ctor")
                AnalyzeStringOrdering(invocationOrCreation, method, context);
        }

        static void AnalyzeToStringInvocation(InvocationExpressionSyntax invocation, IMethodSymbol method, SyntaxNodeAnalysisContext context)
        {
            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
                return;

            ITypeSymbol type = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;

            if (IsBenignStringifiableType(type))
                return;

            if (IsInBenignFormattingContext(invocation, context.SemanticModel))
                return;

            context.ReportDiagnostic(Diagnostic.Create(ToStringRule, invocation.GetLocation(), invocation, type));
        }

        static void AnalyzeStringJoinLikeInvocation(InvocationExpressionSyntax invocation, int numParamsToSkip, IMethodSymbol method, SyntaxNodeAnalysisContext context)
        {
            if (!method.TypeArguments.IsEmpty)
            {
                ITypeSymbol joinedType = method.TypeArguments.Single();
                if (IsBenignStringifiableType(joinedType))
                    return;
                if (IsInBenignFormattingContext(invocation, context.SemanticModel))
                    return;
                context.ReportDiagnostic(Diagnostic.Create(GeneralDefaultFormattedInvocationRule, invocation.GetLocation(), invocation, joinedType));
            }
            else if (method.Parameters[numParamsToSkip].Type.ToString() == "params object?[]" || method.Parameters[numParamsToSkip].Type.ToString() == "object?")
            {
                IEnumerable<ArgumentSyntax> stringifiedArguments = invocation.ArgumentList.Arguments.Skip(numParamsToSkip);

                if (stringifiedArguments.Count() == 1 && context.SemanticModel.GetTypeInfo(stringifiedArguments.Single().Expression).Type.ToString() == "object[]")
                {
                    context.ReportDiagnostic(Diagnostic.Create(GeneralDefaultFormattedInvocationRule, invocation.GetLocation(), invocation, "object"));
                }
                else
                {
                    foreach (ArgumentSyntax arg in stringifiedArguments)
                    {
                        ITypeSymbol argType = context.SemanticModel.GetTypeInfo(arg.Expression).Type;
                        if (IsBenignStringifiableType(argType))
                            continue;
                        if (IsInBenignFormattingContext(invocation, context.SemanticModel))
                            continue;
                        context.ReportDiagnostic(Diagnostic.Create(GeneralDefaultFormattedInvocationRule, invocation.GetLocation(), invocation, argType));
                    }
                }
            }
        }

        static void AnalyzeStringBuilderAppendOrInsertInvocation(InvocationExpressionSyntax invocation, IMethodSymbol method, SyntaxNodeAnalysisContext context)
        {
            int parameterIndex = method.Name == "Append" ? 0
                               : method.Name == "Insert" ? 1
                               : throw new InvalidOperationException($"Unknown StringBuilder method {method.Name} for analysis");

            ArgumentSyntax  arg     = invocation.ArgumentList.Arguments[parameterIndex];
            ITypeSymbol     argType = context.SemanticModel.GetTypeInfo(arg.Expression).Type;

            if (IsBenignStringifiableType(argType))
                return;

            if (IsInBenignFormattingContext(invocation, context.SemanticModel))
                return;

            context.ReportDiagnostic(Diagnostic.Create(GeneralDefaultFormattedInvocationRule, invocation.GetLocation(), invocation, argType));
        }

        static void AnalyzeStringOrdering(SyntaxNode invocationOrObjectCreation, IMethodSymbol method, SyntaxNodeAnalysisContext context)
        {
            if (method.Parameters.Any(p => p.Type.ToString() == "System.Collections.Generic.IComparer<string>?"))
                return;

            string syntaxStr = invocationOrObjectCreation is ObjectCreationExpressionSyntax ? "Object created in" : "Invocation";
            context.ReportDiagnostic(Diagnostic.Create(StringOrderRule, invocationOrObjectCreation.GetLocation(), syntaxStr, Util.KludgeRemoveNewlines(invocationOrObjectCreation.ToString())));
        }

        static bool IsBenignStringifiableType(ITypeSymbol type)
        {
            string typeString = type.ToString();

            // System.Nullable<T> (a.k.a `T?`) is OK iff T is OK.
            if (typeString.EndsWith("?", StringComparison.Ordinal))
            {
                INamedTypeSymbol namedType = (INamedTypeSymbol)type;
                ITypeSymbol underlyingType = namedType.TypeArguments[0];
                return IsBenignStringifiableType(underlyingType);
            }

            // enums are ok
            if (typeString == "System.Enum" || type.BaseType?.ToString() == "System.Enum")
                return true;

            // unsigned integers are presumed ok
            if (typeString == "byte"
             || typeString == "ushort"
             || typeString == "uint"
             || typeString == "ulong")
                return true;

            // Certain types are IFormattable but their stringification
            // does not care about IFormatProvider/culture.
            if (typeString == "char"
             || typeString == "System.Guid")
                return true;

            // object is presumed NOT ok, because we don't know enough about the actual type - it might be IFormattable.
            // This is of course a pretty fuzzy check - other types than object could be suspicious
            // as well, and also not all uses of object are really suspicious if we know enough from
            // the context - but this is probably good enough.
            // Basically we're assuming that if the static type is not object but something more specific,
            // then there's a decent chance (though no guarantee) the caller knows what they're doing.
            if (typeString == "object")
                return false;

            // IFormattables (other than what was ok'd above) are generally NOT ok - should be using their IFormatProvider overloads
            if (Util.IsOrImplementsInterface(type, "System.IFormattable"))
                return false;

            return true;
        }

        static bool IsInBenignFormattingContext(SyntaxNode startNode, SemanticModel semanticModel)
        {
            // "Benign formatting context" means a context where we presume it's not
            // super important that things get formatted in a particular way.
            //
            // We consider startNode to be in a "benign formatting context" if, when
            // ascending from startNode until the first StatementSyntax, we encounter
            // a node that is one of the following:
            // - argument to exception constructor
            // - argument to logging invocation
            //
            // This isn't perfect; just because an expression is contained
            // within e.g. an argument to an exception constructor doesn't mean it
            // cannot have non-benign side-effects. But we presume that such
            // silliness doesn't take place.
            foreach (SyntaxNode ancestor in startNode.AncestorsAndSelf())
            {
                if (IsExceptionConstructorArgument(ancestor, semanticModel)
                 || IsLoggingArgument(ancestor, semanticModel))
                    return true;

                if (ancestor is StatementSyntax)
                    break;
            }

            return false;
        }

        static bool IsExceptionConstructorArgument(SyntaxNode node, SemanticModel semanticModel)
        {
            if (node                is ArgumentSyntax
             && node.Parent         is ArgumentListSyntax
             && node.Parent.Parent  is ObjectCreationExpressionSyntax objectCreation)
            {
                IMethodSymbol constructor = (IMethodSymbol)semanticModel.GetSymbolInfo(objectCreation).Symbol;
                return Util.IsDerivedFrom(constructor.ContainingType, "System.Exception");
            }
            else
                return false;
        }

        static bool IsLoggingArgument(SyntaxNode node, SemanticModel semanticModel)
        {
            if (node                is ArgumentSyntax
             && node.Parent         is ArgumentListSyntax
             && node.Parent.Parent  is InvocationExpressionSyntax invocation)
            {
                IMethodSymbol   method              = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol;
                string          methodName          = method.Name;
                string          containingTypeStr   = method.ContainingType.ToString();

                // \todo [nuutti] Other logging types
                if (containingTypeStr == "Metaplay.Cloud.MetaLoggingAdapter"
                 || containingTypeStr == "Metaplay.Core.DebugLog"
                 || containingTypeStr == "Microsoft.Extensions.Logging.LoggerExtensions")
                {
                    return methodName == "Verbose"
                        || methodName == "Debug"
                        || methodName == "Info"
                        || methodName == "Warn"
                        || methodName == "Warning"
                        || methodName == "Error"
                        || methodName == "Log"
                        || methodName == "LogCritical"
                        || methodName == "LogDebug"
                        || methodName == "LogError"
                        || methodName == "LogInformation"
                        || methodName == "LogTrace"
                        || methodName == "LogWarning";
                }
                else if (containingTypeStr == "System.Console")
                {
                    return methodName == "WriteLine";
                }
                else
                    return false;
            }
            else
                return false;
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class LocalVariableReadAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor LocalIsNeverReadRule = new DiagnosticDescriptor(
            id:             "MP_VAR_00",
            title:          "Local variable is never read",
            messageFormat:  "Local variable '{0}' is never read. Consider removing it, discarding it, or renaming it to '_' .",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(LocalIsNeverReadRule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.MethodDeclaration);
        }

        static void Analyze(SyntaxNodeAnalysisContext context)
        {
            MethodDeclarationSyntax methodDeclaration   = (MethodDeclarationSyntax)context.Node;
            SyntaxNode              methodBody          = TryGetMethodBody(methodDeclaration);

            if (methodBody == null)
                return;

            DataFlowAnalysis        dataFlowAnalysis    = context.SemanticModel.AnalyzeDataFlow(methodBody);

            // Check the variables declared in the method. Certain kinds of declarations
            // (e.g. the declaration in a foreach) are omitted.
            // Report variables that are not read, except variables named "_".
            // Note that this is quite naive, it just checks if there are reads of the variable
            // anywhere, it does not check if a specific assigned value is read.
            foreach (ISymbol declaredSymbol in dataFlowAnalysis.VariablesDeclared)
            {
                string variableName = declaredSymbol.Name;

                if (variableName != "_")
                {
                    bool isRead = dataFlowAnalysis.ReadInside.Contains(declaredSymbol);

                    if (!isRead)
                    {
                        SyntaxNode declaringSyntax = declaredSymbol.DeclaringSyntaxReferences.Single().GetSyntax();
                        if (!ShouldReportDeclaringSyntax(declaringSyntax, dataFlowAnalysis, context.SemanticModel))
                            continue;

                        Location location = declaredSymbol.Locations.Single();
                        context.ReportDiagnostic(Diagnostic.Create(LocalIsNeverReadRule, location, variableName));
                    }
                }
            }
        }

        static SyntaxNode TryGetMethodBody(MethodDeclarationSyntax method)
        {
            return (SyntaxNode)method.Body ?? (SyntaxNode)method.ExpressionBody?.Expression;
        }

        static bool ShouldReportDeclaringSyntax(SyntaxNode declaringSyntax, DataFlowAnalysis dataFlowAnalysis, SemanticModel semanticModel)
        {
            // Don't report variables in tuple deconstruction declaration, if at least one of the variables in the tuple is read.
            // I.e. only report the variables in the tuple declaration if none of them are used.
            if (declaringSyntax                                         is SingleVariableDesignationSyntax
                && declaringSyntax.Parent                               is DeclarationExpressionSyntax
                && declaringSyntax.Parent.Parent                        is ArgumentSyntax
                && declaringSyntax.Parent.Parent.Parent                 is TupleExpressionSyntax tuple
                && declaringSyntax.Parent.Parent.Parent.Parent          is AssignmentExpressionSyntax
                && declaringSyntax.Parent.Parent.Parent.Parent.Parent   is ExpressionStatementSyntax)
            {
                if (AnyTupleDeclaredVariableIsRead(tuple, dataFlowAnalysis, semanticModel))
                    return false;
            }

            // Otherwise, trace ancestors up to and including the closest StatementSyntax,
            // and don't report the variable if certain uninteresting(?) kinds of declaration
            // are encountered.

            SyntaxNode ancestor = declaringSyntax;

            while (true)
            {
                if (ancestor is ParameterSyntax
                 || ancestor is ForEachStatementSyntax
                 || ancestor is ForEachVariableStatementSyntax
                 || ancestor is SwitchStatementSyntax
                 || ancestor is IfStatementSyntax
                 || ancestor is ArgumentListSyntax)
                {
                    return false;
                }

                if (ancestor is StatementSyntax)
                    return true;

                ancestor = ancestor.Parent;
            }
        }

        static bool AnyTupleDeclaredVariableIsRead(TupleExpressionSyntax tuple, DataFlowAnalysis dataFlowAnalysis, SemanticModel semanticModel)
        {
            foreach (ArgumentSyntax argument in tuple.Arguments)
            {
                if (argument.Expression is DeclarationExpressionSyntax declaration)
                {
                    ISymbol symbol = semanticModel.GetSymbolInfo(declaration).Symbol;
                    if (dataFlowAnalysis.ReadInside.Contains(symbol))
                        return true;
                }
            }

            return false;
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class StructInitializerFootgunAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor StructExplicitParameterlessConstructor = new DiagnosticDescriptor(
            id:             "MP_STI_00",
            title:          "Struct has an explicit parameterless instance constructor",
            messageFormat:  "Struct '{0}' has an explicitly-defined parameterless constructor. Parameterless struct instance constructors are misleading footguns because they're not run if the struct is implicitly constructed (e.g. with `default`).",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);
        private static readonly DiagnosticDescriptor StructMemberInitializer = new DiagnosticDescriptor(
            id:             "MP_STI_01",
            title:          "Struct instance member has an initializer",
            messageFormat:  "'{0}' of struct '{1}' is a field or property with an initializer. Struct instance member initializers are misleading footguns because they're not used if the struct is implicitly constructed (e.g. with `default`).",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            StructExplicitParameterlessConstructor,
            StructMemberInitializer);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.StructDeclaration, SyntaxKind.RecordStructDeclaration);
        }

        static void Analyze(SyntaxNodeAnalysisContext context)
        {
            TypeDeclarationSyntax typeDeclaration = (TypeDeclarationSyntax)context.Node;

            foreach (IMethodSymbol constructor in context.SemanticModel.GetDeclaredSymbol(typeDeclaration).InstanceConstructors)
            {
                if (!constructor.Parameters.Any() && constructor.DeclaringSyntaxReferences.Any())
                    context.ReportDiagnostic(Diagnostic.Create(StructExplicitParameterlessConstructor, constructor.DeclaringSyntaxReferences.Single().GetSyntax().GetLocation(), typeDeclaration.Identifier));
            }

            foreach (MemberDeclarationSyntax member in typeDeclaration.Members)
            {
                if (member.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword)
                                                  || modifier.IsKind(SyntaxKind.StaticKeyword)))
                {
                    continue;
                }

                if (member is FieldDeclarationSyntax field)
                {
                    foreach (VariableDeclaratorSyntax declarator in field.Declaration.Variables)
                    {
                        if (declarator.Initializer != null)
                            context.ReportDiagnostic(Diagnostic.Create(StructMemberInitializer, member.GetLocation(), declarator.Identifier, typeDeclaration.Identifier));
                    }
                }
                else if (member is PropertyDeclarationSyntax property)
                {
                    if (property.Initializer != null)
                        context.ReportDiagnostic(Diagnostic.Create(StructMemberInitializer, member.GetLocation(), property.Identifier, typeDeclaration.Identifier));
                }
            }
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ControllerLoggerCategoryAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor MismatchingControllerLoggerCategory = new DiagnosticDescriptor(
            id:             "MP_CLC_00",
            title:          "API controller has mismatching logger category name",
            messageFormat:  "Constructor of API controller '{0}' passes '{1}' as its logger category - expected to pass '{0}' itself",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            MismatchingControllerLoggerCategory);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ConstructorDeclaration);
        }

        static void Analyze(SyntaxNodeAnalysisContext context)
        {
            ConstructorDeclarationSyntax constructorDeclaration = (ConstructorDeclarationSyntax)context.Node;

            if (!constructorDeclaration.ParameterList.Parameters.Any())
                return;

            INamedTypeSymbol containingType = context.SemanticModel.GetDeclaredSymbol(constructorDeclaration).ContainingType;

            if (Util.Ancestors(containingType).Any(t => t.Name == "ControllerBase"))
            {
                foreach (ParameterSyntax param in constructorDeclaration.ParameterList.Parameters)
                {
                    INamedTypeSymbol paramType = context.SemanticModel.GetSymbolInfo(param.Type).Symbol as INamedTypeSymbol;

                    if (paramType == null)
                        continue;
                    if (paramType.Name != "ILogger")
                        continue;
                    if (!paramType.TypeArguments.Any())
                        continue;

                    ITypeSymbol loggerTypeArg = paramType.TypeArguments.Single();
                    if (!SymbolEqualityComparer.Default.Equals(loggerTypeArg, containingType))
                        context.ReportDiagnostic(Diagnostic.Create(MismatchingControllerLoggerCategory, param.Type.GetLocation(), containingType.Name, loggerTypeArg.Name));
                }
            }
        }
    }

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ConversionAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor AsyncLambdaConvertedToVoidReturning = new DiagnosticDescriptor(
            id:             "MP_CON_00",
            title:          "Async lambda is converted to void-returning delegate",
            messageFormat:  "This lambda is async but is converted to void-returning delegate type {0}. Consider converting to Func<..., Task> instead. As written, the delegate will be treated similarly to `async void` methods which likely wasn't intended.",
            category:       "MetaplayCustom",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: false);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            AsyncLambdaConvertedToVoidReturning);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxNodeAction(AnalyzeAnonymousFunctionExpression, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
        }

        static void AnalyzeAnonymousFunctionExpression(SyntaxNodeAnalysisContext context)
        {
            // Warn about async anonymous functions being converted to void-returning delegate.

            AnonymousFunctionExpressionSyntax anonymousFunctionExpression = (AnonymousFunctionExpressionSyntax)context.Node;

            // Ignore non-async.
            if (anonymousFunctionExpression.AsyncKeyword == default)
                return;

            // Resolve the type to which the anonymous function (e.g. lambda) is converted.
            ITypeSymbol convertedType = context.SemanticModel.GetTypeInfo(anonymousFunctionExpression).ConvertedType;

            // Ignore if the type isn't a void-returning delegate type (such as Action<...>).
            if (!(convertedType is INamedTypeSymbol convertedTypeNamedType))
                return;
            if (convertedTypeNamedType.DelegateInvokeMethod == null)
                return;
            if (!convertedTypeNamedType.DelegateInvokeMethod.ReturnsVoid)
                return;

            string convertedTypeStr = convertedType.ToString();

            // Ignore certain scenarios where we assume that the conversion is intentional.
            //
            // System.Threading.TimerCallback: used as the callback for System.Threading.Timer.
            // We ignore this because:
            // - Exceptions thrown from timer callbacks (async or not) go unhandled anyway,
            //   so 'async void' doesn't seem to make it worse.
            // - Timer doesn't support Task-returning callbacks anyway.
            if (convertedTypeStr == "System.Threading.TimerCallback")
                return;

            context.ReportDiagnostic(Diagnostic.Create(AsyncLambdaConvertedToVoidReturning, anonymousFunctionExpression.GetLocation(), convertedTypeStr));
        }
    }
}
