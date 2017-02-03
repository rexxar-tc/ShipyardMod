using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.ModAPI;

namespace ShipyardMod.Utility
{
    public static class Profiler
    {
        [Serializable]
        public class Namespace
        {
            public List<Class> Classes;
            public string Name;

            public Namespace()
            {
            }

            public Namespace(string name)
            {
                Name = name;
                Classes = new List<Class>();
            }
        }

        [Serializable]
        public class Class
        {
            public double AvgRuntime;
            public double MaxRuntime;
            public List<Member> Members;
            public string Name;
            [XmlIgnore]
            public List<double> Runtimes;

            public Class()
            {
            }

            public Class(string name)
            {
                Name = name;
                Members = new List<Member>();
                Runtimes = new List<double>();
            }
        }

        [Serializable]
        public class Member
        {
            public double AvgRuntime;
            public List<Block> Blocks;
            public double MaxRuntime;
            public string Name;
            [XmlIgnore]
            public List<double> Runtimes;

            public Member()
            {
            }

            public Member(string name)
            {
                Name = name;
                Blocks = new List<Block>();
                Runtimes = new List<double>();
            }
        }

        [Serializable]
        public class Block
        {
            public double AvgRuntime;
            public double MaxRuntime;
            public string Name;
            [XmlIgnore]
            public List<double> Runtimes;

            public Block()
            {
            }

            public Block(string name)
            {
                Name = name;
                Runtimes = new List<double>();
            }
        }

        public static ProfilingBlockBase EmptyBlock = new ProfilingBlockBase();

        public class ProfilingBlockBase : IDisposable
        {
            public virtual void End() { }

            public void Dispose()
            {
                End();
            }
        }

        public class ProfilingBlock : ProfilingBlockBase
        {
            public readonly string Block;
            public readonly string Class;
            public readonly string Member;

            public readonly string Namespace;
            public readonly Stopwatch Stopwatch;
            private bool _stopped;

            public ProfilingBlock(string namespaceName, string className, string memberName = null, string blockName = null)
            {
                Namespace = namespaceName;
                Class = className;
                Member = memberName;
                Block = blockName;
                Stopwatch = new Stopwatch();
            }
            
            public override void End()
            {
                if (_stopped)
                    return;

                _stopped = true;

                Profiler.End(this);
            }
        }

        private static readonly List<Namespace> Namespaces = new List<Namespace>();

        public static ProfilingBlockBase Start(string className, string memberName = null, string blockName = null)
        {
            if(!ShipyardCore.Debug)
                return EmptyBlock;

            string[] splits = className.Split('.');

            var profileblock = new ProfilingBlock(splits[1], splits[2], memberName, blockName);
            profileblock.Stopwatch.Start();
            return profileblock;
        }

        private static void End(ProfilingBlock profilingBlock)
        {
            profilingBlock.Stopwatch.Stop();

            Utilities.QueueAction(() =>
                                  {
                                      try
                                      {
                                          double runtime = 1000d * profilingBlock.Stopwatch.ElapsedTicks / Stopwatch.Frequency;

                                          Namespace thisNamespace = Namespaces.FirstOrDefault(n => n.Name == profilingBlock.Namespace);
                                          if (thisNamespace == null)
                                          {
                                              thisNamespace = new Namespace(profilingBlock.Namespace);
                                              Namespaces.Add(thisNamespace);
                                          }
                                          Class thisClass = thisNamespace.Classes.FirstOrDefault(c => c.Name == profilingBlock.Class);
                                          if (thisClass == null)
                                          {
                                              thisClass = new Class(profilingBlock.Class);
                                              thisNamespace.Classes.Add(thisClass);
                                          }

                                          if (profilingBlock.Member == null)
                                          {
                                              if (thisClass.Runtimes.Count >= int.MaxValue)
                                                  thisClass.Runtimes.RemoveAt(0);
                                              thisClass.Runtimes.Add(runtime);
                                              thisClass.MaxRuntime = thisClass.Runtimes.Max();
                                              thisClass.AvgRuntime = thisClass.Runtimes.Average();
                                              return;
                                          }

                                          Member thisMember = thisClass.Members.FirstOrDefault(m => m.Name == profilingBlock.Member);
                                          if (thisMember == null)
                                          {
                                              thisMember = new Member(profilingBlock.Member);
                                              thisClass.Members.Add(thisMember);
                                          }

                                          if (profilingBlock.Block == null)
                                          {
                                              if (thisMember.Runtimes.Count >= int.MaxValue)
                                                  thisMember.Runtimes.RemoveAt(0);
                                              thisMember.Runtimes.Add(runtime);
                                              thisMember.MaxRuntime = thisMember.Runtimes.Max();
                                              thisMember.AvgRuntime = thisMember.Runtimes.Average();
                                              return;
                                          }

                                          Block thisBlock = thisMember.Blocks.FirstOrDefault(b => b.Name == profilingBlock.Block);
                                          if (thisBlock == null)
                                          {
                                              thisBlock = new Block(profilingBlock.Block);
                                              thisMember.Blocks.Add(thisBlock);
                                          }

                                          if (thisBlock.Runtimes.Count >= int.MaxValue)
                                              thisBlock.Runtimes.RemoveAt(0);
                                          thisBlock.Runtimes.Add(runtime);
                                          thisBlock.MaxRuntime = thisBlock.Runtimes.Max();
                                          thisBlock.AvgRuntime = thisBlock.Runtimes.Average();
                                      }
                                      catch (Exception ex)
                                      {
                                          Logging.Instance.WriteLine(ex.ToString());
                                      }
                                  });
        }

        private static bool _saving;

        public static void Save()
        {
            if (_saving)
                return;
            _saving = true;
            MyAPIGateway.Parallel.Start(() =>
                                        {
                                            TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage("profiler.xml", typeof(Profiler));

                                            writer.Write(MyAPIGateway.Utilities.SerializeToXML(Namespaces));
                                            writer.Flush();

                                            writer.Close();
                                            _saving = false;
                                        });
        }
    }
}