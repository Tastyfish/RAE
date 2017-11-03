using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

using Irony.Parsing;

namespace RAE
{
    class Program
    {
        private static void PrintHelp()
        {
            Console.WriteLine("RAE Compiler / Runtime v"
                    + Assembly.GetExecutingAssembly().GetName().Version);
            Console.WriteLine("Usage: rae [-sv] [--name=...] [filename ...]");
            Console.WriteLine("-s\tSaves the assembly.");
            Console.WriteLine("-v\tVerbose compilation.");
            Console.WriteLine("--name\tSet name of output.");
        }

        static void Main(string[] args)
        {
            bool optionSave = false;
            bool optionVerbose = false;
            string asmName = null;
            List<string> files = new List<string>();

            var flagR = new Regex(@"^\-(\w+)$");
            var nameR = new Regex(@"^\-\-name=(\w+)$");
            var starR = new Regex(@"^([^\*]*?)([^\\/*]*\*[^\\/*]*)$");

            foreach (string arg in args)
            {
                Match m = flagR.Match(arg);
                if (m.Success)
                {
                    foreach (char flag in m.Groups[1].Value)
                    {
                        switch (flag)
                        {
                            case 's':
                                optionSave = true;
                                break;
                            case 'v':
                                optionVerbose = true;
                                break;
                        }
                    }
                }
                else
                {
                    m = nameR.Match(arg);
                    if (m.Success)
                    {
                        asmName = m.Groups[1].Value;
                    }
                    else
                    {
                        m = starR.Match(arg);
                        if (m.Success)
                        {
                            foreach (var sf in Directory.GetFiles(m.Groups[1].Value, m.Groups[2].Value, SearchOption.AllDirectories))
                            {
                                if (!files.Contains(sf))
                                    files.Add(sf);
                            }
                        }
                        else if (!files.Contains(arg))
                        {
                            files.Add(arg);
                        }
                    }
                }
            }

            if (files.Count == 0)
            {
                PrintHelp();
                return;
            }

            // default to first file name
            if (asmName == null)
            {
                FileInfo f = new FileInfo(files[0]);
                asmName = f.Name.Substring(0, f.Name.Length - f.Extension.Length);
            }

            Parser p = new Parser(new RAEGrammer());
            ParseTree[] trees = new ParseTree[files.Count];
            bool failed = false;

            if (optionVerbose)
                Console.WriteLine("Will build " + asmName + "...");

            for (int i = 0; i < trees.Length; i++)
            {
                if (optionVerbose)
                    Console.WriteLine("Parsing " + files[i] + "...");

                FileInfo f = new FileInfo(files[i]);
                string fileCnt;
                try
                {
                    fileCnt = f.OpenText().ReadToEnd();
                }
                catch (FileNotFoundException e)
                {
                    Console.WriteLine(e.Message);
                    return;
                }
                trees[i] = p.Parse(fileCnt, f.Name);
                if (trees[i].Status == ParseTreeStatus.Error)
                {
                    foreach (var msg in trees[i].ParserMessages)
                    {
                        Console.WriteLine(f.Name + msg.Location.ToString()
                            + ": " + msg.Message);
                    }
                    Console.WriteLine("" + trees[i].ParserMessages.Count + " errors in " + f.Name);
                    failed = true;
                }
            }

            if (optionVerbose)
                Console.WriteLine("Compiling...");

            if (failed)
                Environment.Exit(2);

            Compiler cpl = new Compiler(asmName)
            {
                Verbose = optionVerbose
            };
            cpl.Compile(trees, files.ToArray(), optionSave);
        }
    }
}
