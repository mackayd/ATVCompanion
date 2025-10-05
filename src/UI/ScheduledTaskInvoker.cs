using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace UI
{
    /// <summary>
    /// Invokes UI.ScheduledTaskCreator.CreateTasks(...) via reflection to tolerate signature variations.
    /// </summary>
    internal static class ScheduledTaskInvoker
    {
        public static bool TryCreateTasks(string exeToRun, string argsWake, out string? error)
        {
            error = null;
            try
            {
                var type = Type.GetType("UI.ScheduledTaskCreator, UI", throwOnError: false);
                if (type == null)
                {
                    error = "ScheduledTaskCreator type not found.";
                    return false;
                }

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                                  .Where(m => m.Name == "CreateTasks")
                                  .ToArray();
                if (methods.Length == 0)
                {
                    error = "No CreateTasks method found on ScheduledTaskCreator.";
                    return false;
                }

                foreach (var m in methods)
                {
                    var ps = m.GetParameters();
                    object? instance = m.IsStatic ? null : Activator.CreateInstance(type);

                    // (string exe, string wake)
                    if (ps.Length == 2 &&
                        ps[0].ParameterType == typeof(string) &&
                        ps[1].ParameterType == typeof(string))
                    {
                        m.Invoke(instance, new object?[] { exeToRun, argsWake });
                        return true;
                    }

                    // (string exe, string wake, out string standby)
                    if (ps.Length == 3 &&
                        ps[0].ParameterType == typeof(string) &&
                        ps[1].ParameterType == typeof(string) &&
                        ps[2].IsOut && ps[2].ParameterType == typeof(string).MakeByRefType())
                    {
                        object?[] a = new object?[] { exeToRun, argsWake, null };
                        m.Invoke(instance, a);
                        return true;
                    }

                    // Heuristic for 4-parameter variants like:
                    // (AppConfig cfg, Action<string,bool> log, out string argsWake, out string argsStandby)
                    if (ps.Length == 4)
                    {
                        var a = new object?[4];
                        for (int i = 0; i < ps.Length; i++)
                        {
                            var p = ps[i];
                            if (p.IsOut)
                            {
                                if (p.ParameterType == typeof(string).MakeByRefType())
                                    a[i] = null;
                                else if (p.ParameterType == typeof(bool).MakeByRefType())
                                    a[i] = false;
                                else
                                    a[i] = null;
                            }
                            else if (p.ParameterType == typeof(string))
                            {
                                // feed exe/wake into the first two string parameters we encounter
                                if (a[0] == null) a[i] = exeToRun;
                                else a[i] = argsWake;
                            }
                            else if (p.ParameterType.FullName == "Core.Config.AppConfig")
                            {
                                // Best-effort: try to load config if available; otherwise pass null
                                try
                                {
                                    var cfgType = Type.GetType("Core.Config.ConfigStore, Core", throwOnError: false);
                                    var appCfgType = Type.GetType("Core.Config.AppConfig, Core", throwOnError: false);
                                    if (cfgType != null && appCfgType != null)
                                    {
                                        var load = cfgType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static);
                                        a[i] = load?.Invoke(null, null);
                                    }
                                    else a[i] = null;
                                }
                                catch { a[i] = null; }
                            }
                            else if (p.ParameterType == typeof(Action<string, bool>))
                            {
                                a[i] = new Action<string, bool>((msg, isErr) => Debug.WriteLine($"{(isErr ? "ERR" : "INF")}: {msg}"));
                            }
                            else
                            {
                                a[i] = null;
                            }
                        }

                        try
                        {
                            m.Invoke(instance, a);
                            return true;
                        }
                        catch { /* try next overload */ }
                    }
                }

                error = "No compatible CreateTasks overload could be invoked.";
                return false;
            }
            catch (Exception ex)
            {
                error = ex.ToString();
                return false;
            }
        }
    }
}
