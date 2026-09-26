using EnvDTE;
using Microsoft.VisualStudio.Shell;
using CodeJanitor.Helpers;
using CodeJanitor.Model.CodeItems;
using CodeJanitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeJanitor.Logic.Cleaning;

/// <summary>
/// A class for encapsulating insertion of explicit access modifier logic.
/// </summary>

internal sealed class InsertExplicitAccessModifierLogic
{
    /// <summary>
    /// The partial keyword.
    /// </summary>
    private const string PartialKeyword = "partial";

    /// <summary>
    /// The singleton instance of the <see cref="InsertExplicitAccessModifierLogic" /> class.
    /// </summary>
    private static InsertExplicitAccessModifierLogic _instance;

    /// <summary>
    /// Gets an instance of the <see cref="InsertExplicitAccessModifierLogic" /> class.
    /// </summary>
    /// <returns>An instance of the <see cref="InsertExplicitAccessModifierLogic" /> class.</returns>

    internal static InsertExplicitAccessModifierLogic GetInstance()
    {
        return _instance ?? (_instance = new InsertExplicitAccessModifierLogic());
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InsertExplicitAccessModifierLogic" /> class.
    /// </summary>

    private InsertExplicitAccessModifierLogic()
    {
    }

    /// <summary>
    /// Inserts the explicit access modifiers on classes where they are not specified.
    /// </summary>
    /// <param name="classes">The classes.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the classes.</param>

    public void InsertExplicitAccessModifiersOnClasses(IEnumerable<CodeItemClass> classes, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnClasses))) return;

        foreach (var codeClass in classes.Select(x => x.CodeClass).Where(y => y is not null))
        {
            var classDeclaration = CodeElementHelper.GetClassDeclaration(codeClass);

            // Skip partial classes - access modifier may be specified elsewhere.
            if (IsKeywordSpecified(classDeclaration, PartialKeyword))
            {
                continue;
            }

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(classDeclaration, codeClass.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeClass.Access = codeClass.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on delegates where they are not specified.
    /// </summary>
    /// <param name="delegates">The delegates.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the delegates.</param>

    public void InsertExplicitAccessModifiersOnDelegates(IEnumerable<CodeItemDelegate> delegates, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnDelegates))) return;

        foreach (var codeDelegate in delegates.Select(x => x.CodeDelegate).Where(y => y is not null))
        {
            var delegateDeclaration = CodeElementHelper.GetDelegateDeclaration(codeDelegate);

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(delegateDeclaration, codeDelegate.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeDelegate.Access = codeDelegate.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on enumerations where they are not specified.
    /// </summary>
    /// <param name="enumerations">The enumerations.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the enumerations.</param>

    public void InsertExplicitAccessModifiersOnEnumerations(IEnumerable<CodeItemEnum> enumerations, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnEnumerations))) return;

        foreach (var codeEnum in enumerations.Select(x => x.CodeEnum).Where(y => y is not null))
        {
            var enumDeclaration = CodeElementHelper.GetEnumerationDeclaration(codeEnum);

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(enumDeclaration, codeEnum.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeEnum.Access = codeEnum.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on events where they are not specified.
    /// </summary>
    /// <param name="events">The events.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the events.</param>

    public void InsertExplicitAccessModifiersOnEvents(IEnumerable<CodeItemEvent> events, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnEvents))) return;

        foreach (var codeEvent in events.Select(x => x.CodeEvent).Where(y => y is not null))
        {
            try
            {
                // Skip events defined inside an interface.
                if (codeEvent.Parent is CodeInterface)
                {
                    continue;
                }

                // Skip explicit interface implementations.
                if (ExplicitInterfaceImplementationHelper.IsExplicitInterfaceImplementation(codeEvent))
                {
                    continue;
                }
            }
            catch (Exception)
            {
                // Skip this event if unable to analyze.
                continue;
            }

            var eventDeclaration = CodeElementHelper.GetEventDeclaration(codeEvent);

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(eventDeclaration, codeEvent.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeEvent.Access = codeEvent.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on fields where they are not specified.
    /// </summary>
    /// <param name="fields">The fields.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the fields.</param>

    public void InsertExplicitAccessModifiersOnFields(IEnumerable<CodeItemField> fields, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnFields))) return;

        foreach (var codeField in fields.Select(x => x.CodeVariable).Where(y => y is not null))
        {
            try
            {
                // Skip "fields" defined inside an enumeration.
                if (codeField.Parent is CodeEnum)
                {
                    continue;
                }
            }
            catch (Exception)
            {
                // Skip this field if unable to analyze.
                continue;
            }

            var fieldDeclaration = CodeElementHelper.GetFieldDeclaration(codeField);

            // Legacy EnvDTE access rewrites can drop the `fixed` keyword on unsafe fixed-size
            // buffers. Skip these declarations until they have a dedicated syntax-aware path.
            if (IsFixedFieldDeclaration(fieldDeclaration))
            {
                continue;
            }

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(fieldDeclaration, codeField.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeField.Access = codeField.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on interfaces where they are not specified.
    /// </summary>
    /// <param name="interfaces">The interfaces.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the interfaces.</param>

    public void InsertExplicitAccessModifiersOnInterfaces(IEnumerable<CodeItemInterface> interfaces, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnInterfaces))) return;

        foreach (var codeInterface in interfaces.Select(x => x.CodeInterface).Where(y => y is not null))
        {
            var interfaceDeclaration = CodeElementHelper.GetInterfaceDeclaration(codeInterface);

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(interfaceDeclaration, codeInterface.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeInterface.Access = codeInterface.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on methods where they are not specified.
    /// </summary>
    /// <param name="methods">The methods.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the methods.</param>

    public void InsertExplicitAccessModifiersOnMethods(IEnumerable<CodeItemMethod> methods, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnMethods))) return;

        foreach (var codeFunction in methods.Select(x => x.CodeFunction).Where(y => y is not null))
        {
            try
            {
                // Skip static constructors - they should not have an access modifier.
                if (codeFunction.IsShared && codeFunction.FunctionKind == vsCMFunction.vsCMFunctionConstructor)
                {
                    continue;
                }

                // Skip destructors - they should not have an access modifier.
                if (codeFunction.FunctionKind == vsCMFunction.vsCMFunctionDestructor)
                {
                    continue;
                }

                // Skip explicit interface implementations.
                if (ExplicitInterfaceImplementationHelper.IsExplicitInterfaceImplementation(codeFunction))
                {
                    continue;
                }

                // Skip methods defined inside an interface.
                if (codeFunction.Parent is CodeInterface)
                {
                    continue;
                }
            }
            catch (Exception)
            {
                // Skip this method if unable to analyze.
                continue;
            }

            var methodDeclaration = CodeElementHelper.GetMethodDeclaration(codeFunction);

            // Skip partial methods - access modifier may be specified elsewhere.
            if (IsKeywordSpecified(methodDeclaration, PartialKeyword))
            {
                continue;
            }

            // Hard stop for generic methods (methods that declare their own type parameter
            // list, e.g. Find<T> or Find<[SomeAttribute] T>): EnvDTE's code model does not
            // correctly regenerate the header when a member's Access property is set on such
            // a method, and can inject the access modifier token at an invalid position
            // (Bug 2: Find<[Attr] T>private ...). See IsGenericMethodDeclaration for the
            // precise detection rule and why a plain generic RETURN type (List<int> GetItems())
            // must NOT trigger this hard stop.
            if (IsGenericMethodDeclaration(methodDeclaration))
            {
                continue;
            }

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(methodDeclaration, codeFunction.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeFunction.Access = codeFunction.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on properties where they are not specified.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the properties.</param>

    public void InsertExplicitAccessModifiersOnProperties(IEnumerable<CodeItemProperty> properties, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnProperties))) return;

        foreach (var codeProperty in properties.Select(x => x.CodeProperty).Where(y => y is not null))
        {
            try
            {
                // Skip explicit interface implementations.
                if (ExplicitInterfaceImplementationHelper.IsExplicitInterfaceImplementation(codeProperty))
                {
                    continue;
                }

                // Skip properties defined inside an interface.
                if (codeProperty.Parent is CodeInterface)
                {
                    continue;
                }
            }
            catch (Exception)
            {
                // Skip this property if unable to analyze.
                continue;
            }

            var propertyDeclaration = CodeElementHelper.GetPropertyDeclaration(codeProperty);

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(propertyDeclaration, codeProperty.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeProperty.Access = codeProperty.Access;
            }
        }
    }

    /// <summary>
    /// Inserts the explicit access modifiers on structs where they are not specified.
    /// </summary>
    /// <param name="structs">The structs.</param>
    /// <param name="settings">The effective cleanup settings of the document containing the structs.</param>

    public void InsertExplicitAccessModifiersOnStructs(IEnumerable<CodeItemStruct> structs, EffectiveCleanupSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.GetBoolean(nameof(Settings.Cleaning_InsertExplicitAccessModifiersOnStructs))) return;

        foreach (var codeStruct in structs.Select(x => x.CodeStruct).Where(y => y is not null))
        {
            var structDeclaration = CodeElementHelper.GetStructDeclaration(codeStruct);

            if (!IsAccessModifierExplicitlySpecifiedOnCodeElement(structDeclaration, codeStruct.Access))
            {
                // Set the access value to itself to cause the code to be added.
                codeStruct.Access = codeStruct.Access;
            }
        }
    }

    /// <summary>
    /// Determines if the access modifier is explicitly defined on the specified code element declaration.
    /// </summary>
    /// <param name="codeElementDeclaration">The code element declaration.</param>
    /// <param name="accessModifier">The access modifier.</param>
    /// <returns>True if access modifier is explicitly specified, otherwise false.</returns>

    private static bool IsAccessModifierExplicitlySpecifiedOnCodeElement(string codeElementDeclaration, vsCMAccess accessModifier)
    {
        string keyword = CodeElementHelper.GetAccessModifierKeyword(accessModifier);

        return IsKeywordSpecified(codeElementDeclaration, keyword);
    }

    /// <summary>
    /// Determines whether the specified field declaration represents an unsafe fixed-size buffer.
    /// </summary>
    /// <param name="fieldDeclaration">The field declaration text.</param>
    /// <returns>True if the declaration contains the <c>fixed</c> keyword, otherwise false.</returns>

    internal static bool IsFixedFieldDeclaration(string fieldDeclaration)
    {
        return IsKeywordSpecified(fieldDeclaration, "fixed");
    }

    /// <summary>
    /// Determines whether the given method declaration text is for a generic method (i.e. one
    /// that declares its own type parameter list, such as <c>Find&lt;T&gt;</c> or
    /// <c>Find&lt;[SomeAttribute] T&gt;</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="CodeElementHelper.GetMethodDeclaration" /> captures declaration text from the
    /// method header up to (but not including) the method's own parameter list. A method that
    /// declares its own type parameters therefore always has that captured text end in
    /// <c>&gt;</c> immediately before the parameter list. A plain generic RETURN type (e.g.
    /// <c>List&lt;int&gt; GetItems()</c>) does not share this shape, since the method name
    /// identifier is captured last and the text ends with that name, not with <c>&gt;</c>.
    /// </remarks>
    /// <param name="methodDeclaration">The method declaration text.</param>
    /// <returns>True if the declaration is for a generic method, otherwise false.</returns>

    internal static bool IsGenericMethodDeclaration(string methodDeclaration)
    {
        var trimmed = methodDeclaration?.TrimEnd();

        return !string.IsNullOrEmpty(trimmed) && trimmed.EndsWith(">", StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines if the specified keyword is present in the specified code element declaration.
    /// </summary>
    /// <param name="codeElementDeclaration">The code element declaration.</param>
    /// <param name="keyword">The keyword.</param>
    /// <returns>True if the keyword is present, otherwise false.</returns>

    private static bool IsKeywordSpecified(string codeElementDeclaration, string keyword)
    {
        string matchString = @"(^|\s)" + keyword + @"\s";

        return RegexNullSafe.IsMatch(codeElementDeclaration, matchString);
    }
}
