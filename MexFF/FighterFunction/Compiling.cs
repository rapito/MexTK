﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace MexTK.FighterFunction
{
    public class Compiling
    {
        private static string StandardPath = @"C:/devkitpro/devkitPPC/";

        public static List<RelocELF> Compile(string[] inputs, bool disableWarnings, bool clean, int optimizationLevel = 2, string[] includes = null, string buildPath = null, bool debugSymbols = false, bool quiet = true)
        {

            //Setting the variable directly by the comparison between Platform and PlatformID for some reason makes linux return an error, so I moved the comparison to an if statement
            bool isWin32;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                isWin32 = true;
            else
                isWin32 = false;

            if (inputs.Length == 0)
                return null;
            
            var devkitpath = isWin32 ? StandardPath : Environment.GetEnvironmentVariable("DEVKITPPC");
            
            if (string.IsNullOrEmpty(devkitpath))
                throw new FileNotFoundException("DEVKITPPC path not set");

            // Set build path to ./build on first input path if not passed
            buildPath = buildPath ?? Path.Combine(Path.GetDirectoryName(inputs[0]), "build");

            // Create Build Path if not exists
            if (!Directory.Exists(buildPath)) Directory.CreateDirectory(buildPath);
            
            // Add build path as include
            var includeList = new List<String>();
            includeList.Add(buildPath);  
            if (includes != null) includeList.AddRange(includes);

            // To make things simple, always include MexTK's path
            var exePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (!includeList.Contains(exePath))
                includeList.Add(exePath);

            // and the include path in mextk
            var exePathInclude = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "include");
            if (!includeList.Contains(exePathInclude))
                includeList.Add(exePathInclude);

            devkitpath = isWin32 ? devkitpath.Replace("/opt/", "C:/") : devkitpath;
            var gccPath = Path.Combine(devkitpath, "bin/powerpc-eabi-gcc");
            var gppPath = Path.Combine(devkitpath, "bin/powerpc-eabi-g++");
            
            if (isWin32)
            {
                gccPath = gccPath + ".exe";
                gppPath = gppPath + ".exe";

                if (!File.Exists(gccPath))
                    gccPath = gccPath.Replace("C:/", "");
                
                if (!File.Exists(gppPath))
                    gppPath = gppPath.Replace("C:/", "");
            }
            
            if (!File.Exists(gccPath))
                throw new FileNotFoundException("powerpc-eabi-gcc bin not found at " + gccPath);
                
            if (!File.Exists(gppPath))
                throw new FileNotFoundException("powerpc-eabi-g++ bin not found at " + gppPath);
                
            List<RelocELF> elfs = new List<RelocELF>();

            // Not used right now but if there's a cpp file, then the linker should be g++
            // since MexTK is it's own linker, not sure how this would affect at all.  
            bool isCppProject = false;          
            
            foreach (var input in inputs)
            {
                var ext = Path.GetExtension(input).ToLower();
                var isCpp = ext.Equals(".cpp");
                
                isCppProject = isCppProject || isCpp;

                if (ext.Equals(".a"))
                    elfs.AddRange(LibArchive.GetElfs(input));

                if (ext.Equals(".o"))
                    elfs.Add(new RelocELF(File.ReadAllBytes(input)));

                if (ext.Equals(".c") || isCpp)
                {
                    using (Process p = new Process())
                    {
                        var outputPath = Path.Combine(buildPath, Path.GetFileNameWithoutExtension(input) + ".o");
                        var outputPathD = Path.Combine(buildPath, Path.GetFileNameWithoutExtension(input) + ".d");
                        
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.WorkingDirectory = Path.GetDirectoryName(input);
                        p.StartInfo.RedirectStandardOutput = true;
                        // Use g++ or gcc as needed
                        p.StartInfo.FileName = isCpp ? gppPath : gccPath;
                        
                        var includesStr = "";
                        if (includeList.Count > 0)
                        {
                            includesStr = $"-I{String.Join(" -I", includeList.ToArray())}";
                        }
                        
                        // add -g for debug symbols
                        p.StartInfo.Arguments = $"-MMD -MP -MF \"{outputPathD}\" {(debugSymbols ? "-g" : "")} {(disableWarnings ? "-w" : "")} -O{optimizationLevel} -Wall -DGEKKO -mogc -mcpu=750 -meabi -mhard-float   {includesStr} -c \"{input}\" -o \"{outputPath}\" -fpermissive";

                        if (!quiet)
                        {
                            Console.WriteLine($"{p.StartInfo.FileName} {p.StartInfo.Arguments}");
                        }
                        p.Start();

                        p.WaitForExit();

                        if (p.ExitCode != 0 || !File.Exists(outputPath))
                        {
                            Console.WriteLine();
                            throw new InvalidDataException($"{Path.GetFileName(input)} failed to compile, see output above for details");
                        }

                        elfs.Add(new RelocELF(File.ReadAllBytes(outputPath)));

                        if (clean)
                        {
                            File.Delete(outputPath);
                            File.Delete(outputPathD);
                        }
                    }
                }
            }

            if (clean)
            {
                if (Directory.Exists(buildPath)) Directory.Delete(buildPath);
            }
            
            return elfs;
        }
    }
}
