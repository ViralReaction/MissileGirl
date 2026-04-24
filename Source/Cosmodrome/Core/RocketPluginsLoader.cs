// // Copyright (c) 2026 ViralReaction
// //
// // This program and the accompanying materials are made available under the
// // terms of the Eclipse Public License 2.0 which is available at
// // http://www.eclipse.org/legal/epl-2.0.
// //
// // SPDX-License-Identifier: EPL-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine.Assertions;
using Verse;

namespace MissileGirl
{
    public sealed class RocketPluginsLoader
    {
        private readonly string[] ApprovedAssemblies = new string[]
        {
            "Gagarin.dll", "Soyuz.dll", "Proton.dll",
        };

        public RocketPluginsLoader()
        {
        }

        public IEnumerable<Assembly> LoadAll()
        {
            List<Assembly> assemblies = new List<Assembly>();
            List<Assembly> dependencies = LoadDirectory(RocketEnvironmentInfo.DependenciesFolderPath).ToList();

            if (RocketEnvironmentInfo.IsDevEnv)
            {
                MissileGirl.Logger.Message($"MissileGirl: Dev environment detected! Loading experimental plugins!");

                assemblies.AddRange(
                    LoadDirectory(RocketEnvironmentInfo.ExperimentalPluginsFolderPath).ToList()
                );
            }
            assemblies.AddRange(
                LoadDirectory(RocketEnvironmentInfo.PluginsFolderPath).ToList()
            );
            return assemblies;
        }

        private IEnumerable<Assembly> LoadDirectory(string directoryPath)
        {
            foreach (string filePath in Directory.GetFiles(directoryPath, "*.dll"))
            {
                string fileName = Path.GetFileName(filePath);
                string assemblyName = Path.GetFileNameWithoutExtension(filePath);
                //if (!ApprovedAssemblies.Contains(fileName) && !RocketAssembliesInfo.ApprovedDependencies.Contains(fileName))
                //{
                //    continue;
                //}
                Logger.Debug($"MissileGirl: Found assembly with name of " +
                             $"<color=red>{assemblyName}</color> and file name of " +
                             $"<color=red>{fileName}</color>");

                yield return LoadAssembly(assemblyName, filePath);
            }
        }

        private Assembly LoadAssembly(string assemblyName, string assemblyPath, string symbolsPath = null)
        {
            try
            {
                if (assemblyPath.Contains(".resources"))
                {
                    return null;
                }
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
                if (assembly != null)
                {
                    return assembly;
                }
                byte[] rawAssembly = ReadAllBytes(assemblyPath);
                byte[] rawSymbolStore = symbolsPath != null ? ReadAllBytes(assemblyPath) : null;
                assembly = rawSymbolStore != null && RocketEnvironmentInfo.IsDevEnv ?
                        AppDomain.CurrentDomain.Load(rawAssembly, rawSymbolStore) :
                        AppDomain.CurrentDomain.Load(rawAssembly);
                Logger.Debug($"MissileGirl: Loaded assembly {assembly?.GetName().FullName} and symbols state is {rawSymbolStore != null && RocketEnvironmentInfo.IsDevEnv}");
                assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
                Logger.Debug($"MissileGirl: Assembly is currently [valid={assembly != null}] and Named {assembly.FullName}");
                if (assembly == null)
                {
                    Logger.Debug($"MissileGirl: Preparing to throw new Exception!");
                    LogAssembliesInDomain();
                    throw new Exception($"MissileGirl: Loaded assembly {assemblyName} not in the " +
                                        $"<color=red>current app domain</color> and path fo {assemblyPath}");
                }
                return assembly;
            }
            catch (Exception er)
            {
                LogAssembliesInDomain();
                Logger.Debug($"MissileGirl: ERROR loading assembly {assemblyName}", exception: er);
                return null;
            }
        }

        private void LogAssembliesInDomain()
        {
            int index = 0;
            string report = "MissileGirl: Assemblies report\n";
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.FullName.Contains("UnityEngine") || a.FullName.Contains("System"))
                    continue;
                report += $"{index++}. {a.FullName}\t{a.GetName().Name}\n";
            }
            Logger.Debug(report);
        }

        private byte[] ReadAllBytes(string filePath)
        {
            return File.ReadAllBytes(filePath);
        }
    }
}
