// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


//------------------------------------------------------------------------------
//

//
// Description: Execution environment for a "C# prime" project. Parses & builds
//              a set of "C# prime" files, then executes the project.
//

#if NETFRAMEWORK
using System.CodeDom.Compiler;
#endif
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
#if NETCOREAPP
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
#endif

namespace MS.Internal.Csp
{
    // This kind of exception is thrown when csp detects an
    // error in the project.
    public class CspProjectException : ApplicationException
    {
        public CspProjectException()
        {
        }

        public CspProjectException(string message) : base(message)
        {
        }
    }

    public class Project
    {
        public class Parameters
        {
            public string[] SourceFiles;
            public string[] ReferencedAssemblies;
            public bool EnableCsPrime = false;
            public bool BreakBeforeInvoke = false;
            public bool DebugModeHack = false;
            public string MainClass;
            public string[] ProjectArgs;
            public string ClrDir;
        }

        //------------------------------------------------------
        //
        //  Constructors
        //
        //------------------------------------------------------

        #region Constructors
        private Project(
            Assembly assembly, 
            TempDirectory tempDirectory,
            bool breakBeforeInvoke
            )
        {
            _assembly = assembly;
            _tempDirectory = tempDirectory;
            _breakBeforeInvoke = breakBeforeInvoke;
        }
        #endregion Constructors

        //------------------------------------------------------
        //
        //  Internal Methods
        //
        //------------------------------------------------------

        #region Internal Methods
        /// <summary>
        /// Parse & compile a C# prime project.
        /// </summary>
        internal static Project Build(
            Parameters parameters
            )
        {
            TempDirectory tempDirectory = new TempDirectory();

            string[] intermediateSource = null;

            if (parameters.EnableCsPrime)
            {
                intermediateSource = CsPrimeParser.Parse(
                    parameters.SourceFiles, 
                    /* sourceDebuggingSupport = */ true
                    );
            }

            ArrayList referencedAssemblies = new ArrayList();

            // Add some implicit referenced assemblies. 

            referencedAssemblies.Add(string.IsNullOrEmpty(parameters.ClrDir) ? "System.dll" : Path.Combine(parameters.ClrDir, "System.dll"));
            if (parameters.EnableCsPrime)
            {
                // Needed so that the project can access MS.Internal.Csp.CsPrimeRuntime.
                // (The parser generates references to it.)
                referencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
            }
            referencedAssemblies.AddRange(parameters.ReferencedAssemblies);

#if NETCOREAPP
            return BuildWithRoslyn(
                parameters,
                tempDirectory,
                intermediateSource,
                (string[])referencedAssemblies.ToArray(typeof(string))
                );
#else
            CompilerParameters cp = new CompilerParameters(
                (string[])referencedAssemblies.ToArray(typeof(string))
                )
            {
                GenerateExecutable = true,
                IncludeDebugInformation = true
            };


            // GenerateInMemory:
            //   If true:  Neither cordbg nor Rascal can find symbols.
            //   If false: TempDirectory.Dispose throws an exception if it tries to
            //             clear the directory, because the CLR still has the .exe file open.

            if (parameters.DebugModeHack)
            {
                cp.GenerateInMemory = false;
                tempDirectory.SetToLeak();    // Since cleanup seems impossible
            }
            else
            {
                cp.GenerateInMemory = true;
            }

            // In order to generate a .pdb that Rascal can use, OutputAssembly
            // must be set to something (i.e. not the default which creates a
            // temporary file).
            //
            // (Hence the futzing with TempDirectory).

            cp.OutputAssembly = Path.Combine(tempDirectory.PathName, "csp.project.exe");

            if (parameters.MainClass != null)
            {
                cp.MainClass = parameters.MainClass;
            }

            Microsoft.CSharp.CSharpCodeProvider codeCompiler = new Microsoft.CSharp.CSharpCodeProvider();

            CompilerResults results;
            if (parameters.EnableCsPrime)
            {
                results = codeCompiler.CompileAssemblyFromSource(cp, intermediateSource);
            }
            else
            {
                results = codeCompiler.CompileAssemblyFromFile(cp, parameters.SourceFiles);
            }

            if (results.Output.Count > 0)
            {
                Console.WriteLine("Output from compiler:");
                foreach(String s in results.Output)
                {
                    Console.WriteLine(s);
                }
            }

            if (results.Errors.Count > 0)
            {
                Console.WriteLine("Aborting.");
                return null;
            }

            return new Project(
                results.CompiledAssembly, 
                tempDirectory, 
                parameters.BreakBeforeInvoke
                );
#endif
        }

        /// <summary>
        /// Execute the Main function. (A static member of a certain hardcoded class).
        /// </summary>
        internal int ExecuteMain(
            string sMainClass,
            string[] parameters
            )
        {
            if (sMainClass == null)
            {
                sMainClass = FindMainClass();
            }

            Object returnValue = null;

            Type tMainClass = _assembly.GetType(sMainClass);
            if (tMainClass == null)
            {
                throw new CspProjectException(
                    "Error: Project does not contain type '" + sMainClass +"'");
            }

            if (!IsMainPresent(tMainClass))
            {
                throw new CspProjectException(
                    "Error: Type '" + sMainClass +"' has no Main method");
            }
            
            try
            {
                if (_breakBeforeInvoke)
                {
                    // This breakpoint is just before we execute the project we've compiled.
                    // If you want to debug the project, step into the following "InvokeMember"
                    // call.

                    System.Diagnostics.Debugger.Break();
                }

                returnValue = 
                tMainClass.InvokeMember(
                    "Main",
                    BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Static,
                    null,
                    null,
                    new object [] {parameters}
                    );
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                ReportException(e.InnerException);
            }
            catch (Exception e)
            {
                ReportException(e);
            }

            if (returnValue is int)
            {
                return (int) returnValue;
            }
            else
            {
                return 0;
            }
        }
        #endregion Internal Methods


        //------------------------------------------------------
        //
        //  Internal Properties
        //
        //------------------------------------------------------


        //------------------------------------------------------
        //
        //  Internal Events
        //
        //------------------------------------------------------


        //------------------------------------------------------
        //
        //  Private Methods
        //
        //------------------------------------------------------

        #region Private Methods
        /// <summary>
        /// Find the class containing the Main function.
        /// </summary>
        private string FindMainClass()
        {
            string sRet = "";

            foreach (Type t in _assembly.GetTypes())
            {
                if (IsMainPresent(t))
                {
                    if (sRet != "")
                    {
                        throw new CspProjectException(
                            "Error: Multiple Main methods - in classes '" + sRet + "' and '" + t.FullName + "'");
                    }

                    sRet = t.FullName;
                }

            }

            if (sRet == "")
            {
                throw new CspProjectException(
                    "Error: No Main method found");
            }

            return sRet;
        }


        /// <summary>
        /// Tests whether the type has a Main method.
        /// Throws an exception if more than one is present.
        /// </summary>
        private bool IsMainPresent(Type t)
        {
            MethodInfo method = null;
            try
            {
                method = t.GetMethod(
                    "Main",
                    BindingFlags.Public | BindingFlags.Static
                    );
            }
            catch (System.Reflection.AmbiguousMatchException)
            {
                throw new CspProjectException(
                    "Error: Multiple Main methods in class '" + t.FullName + "'");
            }

            return method != null;
        }

        /// <summary>
        /// Report an exception to the console.
        /// </summary>
        private static void ReportException(Exception e)
        {
            throw new ApplicationException(e.Message);
        }

#if NETCOREAPP
        private static Project BuildWithRoslyn(
            Parameters parameters,
            TempDirectory tempDirectory,
            string[] intermediateSource,
            string[] referencedAssemblies
            )
        {
            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();

            if (parameters.EnableCsPrime)
            {
                for (int i = 0; i < intermediateSource.Length; i++)
                {
                    syntaxTrees.Add(CreateSyntaxTree(intermediateSource[i], parameters.SourceFiles[i]));
                }
            }
            else
            {
                foreach (string sourceFile in parameters.SourceFiles)
                {
                    syntaxTrees.Add(CreateSyntaxTree(File.ReadAllText(sourceFile), sourceFile));
                }
            }

            CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(
                OutputKind.ConsoleApplication,
                mainTypeName: parameters.MainClass,
                optimizationLevel: OptimizationLevel.Debug
                );

            CSharpCompilation compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(Path.GetRandomFileName()),
                syntaxTrees,
                ResolveMetadataReferences(referencedAssemblies),
                compilationOptions
                );

            string outputAssembly = Path.Combine(tempDirectory.PathName, "csp.project.dll");

            if (parameters.DebugModeHack)
            {
                tempDirectory.SetToLeak();
            }

            using MemoryStream peStream = parameters.DebugModeHack ? null : new MemoryStream();
            using FileStream peFileStream = parameters.DebugModeHack ? File.Create(outputAssembly) : null;
            Stream outputStream = parameters.DebugModeHack ? peFileStream : peStream;

            EmitResult results = compilation.Emit(outputStream);

            ReportDiagnostics(results.Diagnostics);

            if (!results.Success)
            {
                Console.WriteLine("Aborting.");
                return null;
            }

            Assembly compiledAssembly;

            if (parameters.DebugModeHack)
            {
                outputStream.Dispose();
                compiledAssembly = Assembly.LoadFile(outputAssembly);
            }
            else
            {
                peStream.Position = 0;
                compiledAssembly = Assembly.Load(peStream.ToArray());
            }

            return new Project(
                compiledAssembly,
                tempDirectory,
                parameters.BreakBeforeInvoke
                );
        }

        private static SyntaxTree CreateSyntaxTree(string source, string path)
        {
            return CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path,
                Encoding.UTF8
                );
        }

        private static IEnumerable<MetadataReference> ResolveMetadataReferences(string[] referencedAssemblies)
        {
            Dictionary<string, string> trustedPlatformAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string trustedPlatformAssemblyPaths = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");

            foreach (string assemblyPath in trustedPlatformAssemblyPaths.Split(Path.PathSeparator))
            {
                string fileName = Path.GetFileName(assemblyPath);

                if (!trustedPlatformAssemblies.ContainsKey(fileName))
                {
                    trustedPlatformAssemblies.Add(fileName, assemblyPath);
                }
            }

            HashSet<string> metadataPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string assemblyPath in trustedPlatformAssemblies.Values)
            {
                metadataPaths.Add(assemblyPath);
            }

            foreach (string reference in referencedAssemblies)
            {
                string resolvedReference = ResolveMetadataReference(reference, trustedPlatformAssemblies);

                if (resolvedReference != null)
                {
                    metadataPaths.Add(resolvedReference);
                }
            }

            List<MetadataReference> references = new List<MetadataReference>();

            foreach (string path in metadataPaths)
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }

            return references;
        }

        private static string ResolveMetadataReference(
            string reference,
            Dictionary<string, string> trustedPlatformAssemblies
            )
        {
            if (File.Exists(reference))
            {
                return Path.GetFullPath(reference);
            }

            string fileName = Path.GetFileName(reference);

            if (trustedPlatformAssemblies.TryGetValue(fileName, out string trustedPlatformAssembly))
            {
                return trustedPlatformAssembly;
            }

            return null;
        }

        private static void ReportDiagnostics(IEnumerable<Diagnostic> diagnostics)
        {
            bool wroteHeader = false;

            foreach (Diagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Hidden)
                {
                    continue;
                }

                if (!wroteHeader)
                {
                    Console.WriteLine("Output from compiler:");
                    wroteHeader = true;
                }

                Console.WriteLine(diagnostic.ToString());
            }
        }
#endif


        #endregion Private Methods


        //------------------------------------------------------
        //
        //  Private Fields
        //
        //------------------------------------------------------

        #region Private Fields
        private Assembly _assembly;
        private TempDirectory _tempDirectory;
        private bool _breakBeforeInvoke;
        #endregion Private Fields
    }
}
