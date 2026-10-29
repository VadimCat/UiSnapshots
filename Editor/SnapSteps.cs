using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapStepInfo
    {
        public string Name;
        public MethodInfo Method;
        public ParameterInfo[] Parameters; // without the leading SnapContext
        public string Signature => $"{Name}({string.Join(", ", Parameters.Select(p => $"{p.Name}{(p.IsOptional ? "?" : "")}: {p.ParameterType.Name}"))})";
    }

    /// <summary>Discovers [SnapStep] methods in every loaded Editor assembly and binds JSON arguments to them.</summary>
    public static class SnapSteps
    {
        private static Dictionary<string, SnapStepInfo> _steps;

        public static IReadOnlyDictionary<string, SnapStepInfo> All
        {
            get
            {
                if (_steps != null) return _steps;
                _steps = new Dictionary<string, SnapStepInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var method in TypeCache.GetMethodsWithAttribute<SnapStepAttribute>())
                {
                    var attribute = method.GetCustomAttribute<SnapStepAttribute>();
                    var parameters = method.GetParameters();
                    if (!method.IsStatic || parameters.Length == 0 || parameters[0].ParameterType != typeof(SnapContext))
                    {
                        Debug.LogError($"[UiSnapshots] {method.DeclaringType}.{method.Name}: a [SnapStep] must be static with SnapContext as its first parameter.");
                        continue;
                    }
                    if (_steps.TryGetValue(attribute.Name, out var existing))
                    {
                        Debug.LogError($"[UiSnapshots] Step '{attribute.Name}' is declared twice: {existing.Method.DeclaringType}.{existing.Method.Name} and {method.DeclaringType}.{method.Name}.");
                        continue;
                    }
                    _steps[attribute.Name] = new SnapStepInfo { Name = attribute.Name, Method = method, Parameters = parameters.Skip(1).ToArray() };
                }
                return _steps;
            }
        }

        public static object[] Bind(SnapStepInfo info, SnapStepCall call, SnapContext context)
        {
            var args = new object[info.Parameters.Length + 1];
            args[0] = context;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < info.Parameters.Length; i++)
            {
                var p = info.Parameters[i];
                JToken token = null;
                var named = call.Args.Properties().FirstOrDefault(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                if (named != null) { token = named.Value; used.Add(named.Name); }
                else if (i == 0 && call.Value != null) token = call.Value;

                if (token == null)
                {
                    if (!p.IsOptional) throw new SnapException($"Step '{info.Name}' needs '{p.Name}'. Signature: {info.Signature}");
                    args[i + 1] = p.DefaultValue is DBNull ? null : p.DefaultValue;
                    continue;
                }
                args[i + 1] = Convert(token, p.ParameterType, info, p.Name);
            }
            var unknown = call.Args.Properties().Select(x => x.Name).Where(n => !used.Contains(n)).ToArray();
            if (unknown.Length > 0) throw new SnapException($"Step '{info.Name}' has no parameter {string.Join(", ", unknown)}. Signature: {info.Signature}");
            return args;
        }

        private static object Convert(JToken token, Type type, SnapStepInfo info, string name)
        {
            if (typeof(JToken).IsAssignableFrom(type)) return token;
            if (type == typeof(string[]) && token.Type == JTokenType.String) return new[] { (string)token };
            if (type == typeof(int[]) && token.Type == JTokenType.Integer) return new[] { (int)token };
            try { return token.ToObject(type); }
            catch (Exception e) { throw new SnapException($"Step '{info.Name}': '{name}' expects {type.Name}, got {token.ToString(Newtonsoft.Json.Formatting.None)} ({e.Message})"); }
        }

        public static async Task<object> Invoke(SnapStepInfo info, object[] args)
        {
            object result;
            try { result = info.Method.Invoke(null, args); }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
            switch (result)
            {
                case Task task:
                    await task;
                    var property = task.GetType().GetProperty("Result");
                    var value = property != null && task.GetType().IsGenericType ? property.GetValue(task) : null;
                    return value != null && value.GetType().Name == "VoidTaskResult" ? null : value;
                case Awaitable awaitable:
                    await awaitable;
                    return null;
                default:
                    return result;
            }
        }
    }
}
