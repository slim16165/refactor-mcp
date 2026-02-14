public static partial class MoveMethodAst
{
    // ===== HELPER METHODS =====

    private static bool HasInstanceMemberUsage(MethodDeclarationSyntax method, HashSet<string> knownMembers)
    {
        var usageChecker = new InstanceMemberUsageChecker(knownMembers);
        usageChecker.Visit(method);
        return usageChecker.HasInstanceMemberUsage;
    }

    private static bool HasMethodCalls(MethodDeclarationSyntax method, HashSet<string> methodNames)
    {
        var callChecker = new MethodCallChecker(methodNames);
        callChecker.Visit(method);
        return callChecker.HasMethodCalls;
    }

    private static bool HasStaticFieldReferences(MethodDeclarationSyntax method, HashSet<string> staticFieldNames)
    {
        var fieldChecker = new StaticFieldChecker(staticFieldNames);
        fieldChecker.Visit(method);
        return fieldChecker.HasStaticFieldReferences;
    }

    // ===== LEGACY STRING-BASED METHODS (for backward compatibility) =====

    public static string MoveStaticMethodInSource(string sourceText, string methodName, string targetClass)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var moveResult = MoveStaticMethodAst(root, methodName, targetClass);
        var finalRoot = AddMethodToTargetClass(moveResult.NewSourceRoot, targetClass, moveResult.MovedMethod, moveResult.Namespace);

        var formatted = Formatter.Format(finalRoot, RefactoringHelpers.SharedWorkspace);
        return formatted.ToFullString();
    }

    public static string MoveInstanceMethodInSource(string sourceText, string sourceClass, string methodName, string targetClass, string accessMemberName, string accessMemberType)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var moveResult = MoveInstanceMethodAst(
            root,
            sourceClass,
            methodName,
            targetClass,
            accessMemberName,
            accessMemberType,
            Array.Empty<string>());
        var finalRoot = AddMethodToTargetClass(moveResult.NewSourceRoot, targetClass, moveResult.MovedMethod, moveResult.Namespace);

        var formatted = Formatter.Format(finalRoot, RefactoringHelpers.SharedWorkspace);
        return formatted.ToFullString();
    }

    public static string MoveMultipleInstanceMethodsInSource(string sourceText, string sourceClass, string[] methodNames, string targetClass, string accessMemberName, string accessMemberType)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        foreach (var methodName in methodNames)
        {
            var moveResult = MoveInstanceMethodAst(
                root,
                sourceClass,
                methodName,
                targetClass,
                accessMemberName,
                accessMemberType,
                Array.Empty<string>());
            root = AddMethodToTargetClass(moveResult.NewSourceRoot, targetClass, moveResult.MovedMethod, moveResult.Namespace);
        }

        var formatted = Formatter.Format(root, RefactoringHelpers.SharedWorkspace);
        return formatted.ToFullString();
    }

    private static string? GetSimpleTypeName(TypeSyntax type)
    {
        return type switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            _ => null
        };
    }

    private static HashSet<string> GetInstanceMemberNames(ClassDeclarationSyntax originClass)
    {
        var root = originClass.SyntaxTree.GetRoot();

        var classCollector = new ClassCollectorWalker();
        classCollector.Visit(root);

        var interfaceCollector = new InterfaceCollectorWalker();
        interfaceCollector.Visit(root);

        var queue = new Queue<MemberDeclarationSyntax>();
        var visited = new HashSet<string>();

        queue.Enqueue(originClass);
        visited.Add(originClass.Identifier.ValueText);

        var walker = new InstanceMemberNameWalker();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            walker.Visit(current);

            if (current is ClassDeclarationSyntax cls && cls.BaseList != null)
            {
                foreach (var bt in cls.BaseList.Types)
                {
                    var name = GetSimpleTypeName(bt.Type);
                    if (name == null || !visited.Add(name))
                        continue;

                    if (classCollector.Classes.TryGetValue(name, out var bClass))
                        queue.Enqueue(bClass);
                    if (interfaceCollector.Interfaces.TryGetValue(name, out var iface))
                        queue.Enqueue(iface);
                }
            }
            else if (current is InterfaceDeclarationSyntax iface && iface.BaseList != null)
            {
                foreach (var bt in iface.BaseList.Types)
                {
                    var name = GetSimpleTypeName(bt.Type);
                    if (name == null || !visited.Add(name))
                        continue;

                    if (interfaceCollector.Interfaces.TryGetValue(name, out var nestedIface))
                        queue.Enqueue(nestedIface);
                }
            }
        }

        return walker.Names;
    }

    // New: Get method names in the class
    private static HashSet<string> GetMethodNames(ClassDeclarationSyntax originClass)
    {
        var root = originClass.SyntaxTree.GetRoot();

        var classCollector = new ClassCollectorWalker();
        classCollector.Visit(root);

        var interfaceCollector = new InterfaceCollectorWalker();
        interfaceCollector.Visit(root);

        var queue = new Queue<MemberDeclarationSyntax>();
        var visited = new HashSet<string>();

        queue.Enqueue(originClass);
        visited.Add(originClass.Identifier.ValueText);

        var walker = new MethodNameWalker();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            walker.Visit(current);

            if (current is ClassDeclarationSyntax cls && cls.BaseList != null)
            {
                foreach (var bt in cls.BaseList.Types)
                {
                    var name = GetSimpleTypeName(bt.Type);
                    if (name == null || !visited.Add(name))
                        continue;

                    if (classCollector.Classes.TryGetValue(name, out var bClass))
                        queue.Enqueue(bClass);
                    if (interfaceCollector.Interfaces.TryGetValue(name, out var iface))
                        queue.Enqueue(iface);
                }
            }
            else if (current is InterfaceDeclarationSyntax iface && iface.BaseList != null)
            {
                foreach (var bt in iface.BaseList.Types)
                {
                    var name = GetSimpleTypeName(bt.Type);
                    if (name == null || !visited.Add(name))
                        continue;

                    if (interfaceCollector.Interfaces.TryGetValue(name, out var nestedIface))
                        queue.Enqueue(nestedIface);
                }
            }
        }

        return walker.Names;
    }

    // New: Get static field names in the class
    private static HashSet<string> GetStaticFieldNames(ClassDeclarationSyntax originClass)
    {
        var walker = new StaticFieldNameWalker();
        walker.Visit(originClass);
        return walker.Names;
    }

    private static HashSet<string> GetNestedClassNames(ClassDeclarationSyntax originClass)
    {
        var walker = new NestedClassNameWalker(originClass);
        walker.Visit(originClass);
        return walker.Names;
    }

    private static Dictionary<string, TypeSyntax> GetPrivateFieldInfos(ClassDeclarationSyntax originClass)
    {
        var walker = new PrivateFieldInfoWalker();
        walker.Visit(originClass);
        return walker.Infos;
    }

    private static HashSet<string> GetUsedPrivateFields(MethodDeclarationSyntax method, HashSet<string> privateFieldNames)
    {
        var walker = new PrivateFieldUsageWalker(privateFieldNames);
        walker.Visit(method);
        return walker.UsedFields;
    }

    private static HashSet<string> GetImplicitInstanceMembers(MethodDeclarationSyntax method)
    {
        var walker = new ImplicitInstanceMemberWalker();
        walker.Visit(method);
        return walker.Members;
    }

    private static bool MemberExists(ClassDeclarationSyntax classDecl, string memberName)
    {
        var walker = new InstanceMemberNameWalker();
        walker.Visit(classDecl);
        return walker.Names.Contains(memberName);
    }

    internal static string GenerateAccessMemberName(IEnumerable<string> existingNames, string targetClass)
    {
        var baseName = "_" + char.ToLower(targetClass[0]) + targetClass.Substring(1);
        var name = baseName;
        var counter = 1;
        var nameSet = new HashSet<string>(existingNames);
        while (nameSet.Contains(name))
        {
            name = baseName + counter;
            counter++;
        }
        return name;
    }

    private static MemberDeclarationSyntax CreateAccessMember(string accessMemberType, string accessMemberName, string targetClass)
    {
        if (accessMemberType == "property")
        {
            return SyntaxFactory.PropertyDeclaration(SyntaxFactory.ParseTypeName(targetClass), accessMemberName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
        }

        return SyntaxFactory.FieldDeclaration(
                    SyntaxFactory.VariableDeclaration(
                        SyntaxFactory.ParseTypeName(targetClass),
                        SyntaxFactory.SeparatedList(new[]
                        {
                            SyntaxFactory.VariableDeclarator(accessMemberName)
                                .WithInitializer(SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.ObjectCreationExpression(SyntaxFactory.ParseTypeName(targetClass))
                                        .WithArgumentList(SyntaxFactory.ArgumentList())))
                        })))
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword));
    }

    // ===== DEPENDENCY ANALYSIS FOR ORDERING =====

    internal static Dictionary<string, HashSet<string>> BuildDependencies(
        SyntaxNode sourceRoot,
        string[] sourceClasses,
        string[] methodNames,
        SemanticModel? semanticModel = null)
    {
        var opSet = sourceClasses.Zip(methodNames, (c, m) => $"{c}.{m}").ToHashSet();
        var collector = new MethodCollectorWalker(opSet);
        collector.Visit(sourceRoot);
        var map = collector.Methods;

        var methodNameSet = methodNames.ToHashSet();
        var deps = new Dictionary<string, HashSet<string>>();

        for (int i = 0; i < sourceClasses.Length; i++)
        {
            var key = $"{sourceClasses[i]}.{methodNames[i]}";
            if (!map.TryGetValue(key, out var method))
            {
                deps[key] = new HashSet<string>();
                continue;
            }

            HashSet<string> called;
            if (semanticModel != null)
            {
                called = BuildSemanticDependencies(method, semanticModel, opSet);
            }
            else
            {
                var walker = new MethodDependencyWalker(methodNameSet);
                walker.Visit(method);

                called = walker.Dependencies
                    .Select(name => $"{sourceClasses[i]}.{name}")
                    .Where(n => map.ContainsKey(n))
                    .ToHashSet();
            }

            deps[key] = called;
        }

        return deps;
    }

    private static HashSet<string> BuildSemanticDependencies(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        HashSet<string> targetFullNames)
    {
        var dependencies = new HashSet<string>();
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var symbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbol != null)
            {
                // Use ToDisplayString for accurate identification including namespace and nested types
                // We fallback to Name if ToDisplayString is too verbose, but for identification we need full qualification
                var container = symbol.ContainingType.Name; // heuristic: still use Name for matching against simple class names in sourceClasses
                var fullName = $"{container}.{symbol.Name}";
                
                if (targetFullNames.Contains(fullName))
                {
                    dependencies.Add(fullName);
                }
            }
        }
        return dependencies;
    }

    internal static List<int> OrderOperations(
        SyntaxNode sourceRoot,
        string[] sourceClasses,
        string[] methodNames,
        SemanticModel? semanticModel = null)
    {
        var deps = BuildDependencies(sourceRoot, sourceClasses, methodNames, semanticModel);
        var indices = Enumerable.Range(0, sourceClasses.Length).ToList();
        return TopologicalSort(indices, deps, sourceClasses, methodNames);
    }

    private static List<int> TopologicalSort(
        List<int> indices,
        Dictionary<string, HashSet<string>> deps,
        string[] sourceClasses,
        string[] methodNames)
    {
        var result = new List<int>();
        var visited = new HashSet<int>();
        var recursionStack = new HashSet<int>();

        void Visit(int i)
        {
            if (visited.Contains(i)) return;
            if (recursionStack.Contains(i))
            {
                 // Cycle detected: stop recursion, but still add this node to result to ensure it's processed.
                 // The order within the cycle will be arbitrary but deterministic based on original list order.
                 return;
            }

            recursionStack.Add(i);

            var key = $"{sourceClasses[i]}.{methodNames[i]}";
            if (deps.TryGetValue(key, out var connections))
            {
                foreach (var depKey in connections)
                {
                    // Find the index for this dependency
                    for (int j = 0; j < sourceClasses.Length; j++)
                    {
                        if ($"{sourceClasses[j]}.{methodNames[j]}" == depKey)
                        {
                            Visit(j);
                            break;
                        }
                    }
                }
            }

            recursionStack.Remove(i);
            visited.Add(i);
            result.Add(i);
        }

        foreach (var i in indices)
        {
            Visit(i);
        }

        return result;
    }
}
