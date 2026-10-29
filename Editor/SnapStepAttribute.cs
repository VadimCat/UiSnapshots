using System;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>
    /// Registers a static method as a scenario step. Signature:
    /// <c>static Task|Awaitable|void Name(SnapContext context, ...parameters)</c>.
    /// JSON arguments bind by parameter name; a scalar step value binds to the first parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SnapStepAttribute : Attribute
    {
        public string Name { get; }
        public SnapStepAttribute(string name) => Name = name;
    }
}
