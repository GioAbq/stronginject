using System;
using System.Diagnostics.CodeAnalysis;

namespace StrongInject.Generator
{
    /// <summary>
    /// Provides options to configure a registration
    /// </summary>
    [Flags]
    [SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "'Default' is the public API name for the zero value; renaming to 'None' would break consumers.")]
    [SuppressMessage("Design", "CA1069:Enums should not have duplicate values", Justification = "FactoryTargetScope bits are intentionally packed; the zero pattern coincides with Default by design.")]
    [SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "'Options' is established public API; renaming would break consumers.")]
    public enum Options : long
    {
        Default = 0,

        #region As Options (bits 0 - 23)
        
        AsImplementedInterfaces = 1L << 0,
        
        AsBaseClasses = 1L << 1,
        
        UseAsFactory = 1L << 2,
        
        ApplySameOptionsToFactoryTargets = 1L << 3,

        #endregion

        #region FactoryTargetScope Options (bits 24 - 31)
        
        FactoryTargetScopeShouldBeInstancePerResolution = Scope.InstancePerResolution << 24,
        
        FactoryTargetScopeShouldBeInstancePerDependency = Scope.InstancePerDependency << 24,
        
        FactoryTargetScopeShouldBeSingleInstance = Scope.SingleInstance << 24,

        #endregion

        #region Other Options (bits 32 - 63)
        
        DoNotDecorate = 1L << 32

        #endregion
    }
}
