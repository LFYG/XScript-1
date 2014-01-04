﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using NewLife.Reflection;
using System.CodeDom.Compiler;
using System.Reflection;
using System.Diagnostics;

namespace NewLife.XScript
{
    /// <summary>脚本</summary>
    public class Script
    {
        private static ScriptConfig _Config;
        /// <summary>脚本配置</summary>
        public static ScriptConfig Config { get { return _Config; } set { _Config = value; } }

        public static void Process(String file, ScriptConfig config)
        {
            Config = config;

            var code = Helper.ReadCode(file);

            // 分析要导入的第三方程序集。默认包含XScript所在目录的所有程序集
            code = "//Assembly=" + AppDomain.CurrentDomain.BaseDirectory + Environment.NewLine + code;
            // 以及源代码所在目录的所有程序集
            code = "//Assembly=" + Path.GetDirectoryName(file) + Environment.NewLine + code;
            var rs = Helper.ParseAssembly(code);
            rs = Helper.ExpendAssembly(rs);

            //var vPath = Environment.GetEnvironmentVariable("Path");
            //Environment.SetEnvironmentVariable("Path", vPath += ";" + Path.GetDirectoryName(file));
            Environment.CurrentDirectory = Path.GetDirectoryName(file);

            var session = ScriptEngine.Create(code, false);

            // 加入代码中标明的程序集
            if (rs.Length > 0) session.ReferencedAssemblies.AddRange(rs);
            // 加入参数中标明的程序集
            if (!String.IsNullOrEmpty(Config.Assembly))
            {
                rs = Config.Assembly.Split(';');
                rs = Helper.ExpendAssembly(rs);
                if (rs.Length > 0) session.ReferencedAssemblies.AddRange(rs);
            }

            // 调试状态下输出最终代码
            if (Config.Debug)
            {
                session.GenerateCode();
                //File.WriteAllText(String.Format("{0:yyyyMMdd_HHmmss_fff}.cs", DateTime.Now), se.FinalCode);
                var codefile = Path.ChangeExtension(file, "code.cs");
                File.WriteAllText(codefile, session.FinalCode);
            }

            // 生成Exe
            if (Config.Exe)
            {
                MakeExe(session, file);

                return;
            }

            Run(session);
        }

        static void MakeExe(ScriptEngine session, String codefile)
        {
            var exe = Path.ChangeExtension(codefile, "exe");
            var option = new CompilerParameters();
            option.OutputAssembly = exe;
            option.GenerateExecutable = true;
            option.GenerateInMemory = false;
            option.IncludeDebugInformation = Config.Debug;

            // 生成图标
            if (!Config.NoLogo)
            {
                var ico = "leaf.ico".GetFullPath();
                option.CompilerOptions = String.Format("/win32icon:\"{0}\"", ico);
                if (!File.Exists(ico))
                {
                    var ms = Assembly.GetEntryAssembly().GetManifestResourceStream("NewLife.XScript.leaf.ico");
                    File.WriteAllBytes(ico, ms.ReadBytes());
                }
            }

            var code = session.FinalCode;

            //// 加上版权信息
            //code = "\r\n[assembly: System.Reflection.AssemblyCompany(\"新生命开发团队\")]\r\n[assembly: System.Reflection.AssemblyCopyright(\"(C)2002-2013 新生命开发团队\")]\r\n[assembly: System.Reflection.AssemblyVersion(\"1.0.*\")]\r\n" + code;

            var cr = session.Compile(code, option);
            if (cr.Errors == null || !cr.Errors.HasErrors)
            {
                Console.WriteLine("已生成{0}", exe);
            }
            else
            {
                //var err = cr.Errors[0];
                //Console.WriteLine("{0} {1} {2}({3},{4})", err.ErrorNumber, err.ErrorText, err.FileName, err.Line, err.Column);
                //Console.WriteLine(cr.Errors[0].ToString());
                Console.WriteLine("编译出错：");
                foreach (var item in cr.Errors)
                {
                    Console.WriteLine(item.ToString());
                }
            }
        }

        static void Run(ScriptEngine session)
        {
            // 预编译
            session.Compile();

            //// 提前加载引用
            //foreach (var item in session.ReferencedAssemblies)
            //{
            //    try
            //    {
            //        Assembly.LoadFile(item);
            //    }
            //    catch { }
            //}

            // 考虑到某些要引用的程序集在别的目录
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);

            var sw = new Stopwatch();
            var times = Config.Times;
            if (times < 1) times = 1;
            while (times-- > 0)
            {
                if (!Config.NoTime)
                {
                    sw.Reset();
                    sw.Start();
                }

                session.Invoke();

                if (!Config.NoTime)
                {
                    sw.Stop();

                    var old = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("执行时间：{0}", sw.Elapsed);
                    //Console.WriteLine("按c键重复执行，其它键退出！");
                    Console.ForegroundColor = old;
                }
            }
        }

        static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = args.Name;
            if (!String.IsNullOrEmpty(name))
            {
                // 遍历现有程序集
                foreach (var item in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (item.FullName == name) return item;
                }

                // 查找当前目录的程序集，就是源代码所在目录
                var p = name.IndexOf(",");
                if (p >= 0) name = name.Substring(0, p);
                var fs = Directory.GetFiles(Environment.CurrentDirectory, name + ".dll", SearchOption.AllDirectories);
                if (fs != null && fs.Length > 0)
                {
                    // 可能多个，遍历加载
                    foreach (var item in fs)
                    {
                        try
                        {
                            var asm = Assembly.LoadFile(item);
                            if (asm != null && asm.FullName == args.Name) return asm;
                        }
                        catch { }
                    }
                }
            }

            return null;
        }
    }
}