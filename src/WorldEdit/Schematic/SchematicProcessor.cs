using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WorldEdit.Output;

namespace WorldEdit.Schematic
{
    public static class SchematicProcessor
    {
        public static void SchematicCommandProcessor(string[] args)
        {
            var s = new MinecraftCommandService();
            var target = new Position(0, 0, 0);
            var shift = new Position(0, 0, 0);
            var rotation = Rotate.None;

            var command = args[0];

            var FileName = "";
            var points = new List<Point>();
            Schematic schematic;
            if (args.Length > 1)
            {
                FileName = args[1];
                if (!FileName.EndsWith("schematic"))
                {
                    FileName += ".schematic";
                }
                var combine = Path.Combine(ConfigurationManager.AppSettings["data"], FileName);
                if (!File.Exists(FileName) && File.Exists(combine))
                {
                    FileName = combine;
                }
                schematic = Schematic.LoadFromFile(FileName);
                points = schematic.GetPoints();
            }
            var outputFilename = Path.GetFileNameWithoutExtension(FileName) + ".fill";
            if (args.Length > 2)
            {
                outputFilename = args[2];
            }
            if (args.Length == 6)
            {
                target.X = Convert.ToInt32(args[3]);
                target.Y = Convert.ToInt32(args[4]);
                target.Z = Convert.ToInt32(args[5]);
            }

            switch (command)
            {
                case "list":
                    
                    var files = Directory.GetFiles(ConfigurationManager.AppSettings["data"], "*.schematic");
                    foreach (var file in files)
                    {
                        s.Status(Path.GetFileName(file));
                    }

                    break;
                case "analyze":
                    var results = ModelAnalyzer.Analyze(points);
                    var firstGroundLayer =
                        results.Layers.First(a => a.Blocks.Any(b => b.Block.Equals("air") && b.PercentOfLayer >= 0.5)).Y;
                    string output = $"{Path.GetFileName(FileName)} Model Size: X:{results.Width} Y:{results.Height} Z:{results.Length} Ground Level:{firstGroundLayer} Total Blocks:{results.Width*results.Height*results.Length}";
                    Console.WriteLine(output);
                    s.Status(output);
                    break;
                case "import":
                    if (args.Length >= 5)
                    {
                        if (args[2].StartsWith("~")||args[3].StartsWith("~")||args[4].StartsWith("~"))
                        {
                            //get current position
                            //parse and add to current position.
                            s.Status("relative positions are not supported.");
                            return;                            
                        }
                        else
                        {
                            target.X = Convert.ToInt32(args[2]);
                            target.Y = Convert.ToInt32(args[3]);
                            target.Z = Convert.ToInt32(args[4]);
                        }
                    }
                    if (args.Length >= 6)
                    {
                        rotation = (Rotate) Convert.ToInt32(args?[5]);
                    }
                    if (args.Length >= 9)
                    {
                        shift.X = Convert.ToInt32(args[6]);
                        shift.Y = Convert.ToInt32(args[7]);
                        shift.Z = Convert.ToInt32(args[8]);
                    }

                    Console.WriteLine($"importing {FileName} to {target}");
                    SendCommandsToCodeConnection(target, points, rotation, shift);
                    break;
                default:
                    break;
            }
            Console.WriteLine($"{FileName}  ${outputFilename} {target.X} {target.Y} {target.Z}");
        }


        private static void SendCommandsToCodeConnection(Position target, List<Point> points, Rotate rotation,
            Position clip = null)
        {
            var service = new MinecraftCommandService();
            var sw = new Stopwatch();


            service.Status("preparing schematic");

            if (clip != null)
            {
                points =
                    points.Where(a => a.X >= clip.X && a.Y >= clip.Y && a.Z >= clip.Z)
                        .Select(a => a.Shift(clip.Muliply(-1)))
                        .ToList();
            }
            if (rotation != Rotate.None)
            {
                sw.Start();
                Console.WriteLine($"rotating points...");
                var rotatedPoints = points.AsParallel().Select(a => a.Rotate(rotation)).ToList();
                Console.WriteLine($"time to rotate {sw.Elapsed}");
                sw.Reset();
                var measures = ModelAnalyzer.Analyze(rotatedPoints);
                sw.Start();
                Console.WriteLine($"shifting points...");
                points = rotatedPoints.AsParallel().Select(a => a.Shift(measures.Minimum.Muliply(-1))).ToList();
                Console.WriteLine($"time to shift {sw.Elapsed}");
                sw.Reset();
            }
            sw.Start();
            Console.WriteLine($"combining points...");
            var exportLines = ConvertFileToCommands(points.Where(a => a.BlockId != 0).ToList());
            Console.WriteLine($"time to combine {sw.Elapsed}");
            sw.Reset();
            sw.Start();
            var shift = exportLines.AsParallel().Select(a => a.Shift(target)).ToList();
            Console.WriteLine($"time to shift {sw.Elapsed}");
            sw.Reset();
            sw.Start();
            var importLines =
                shift.AsParallel().OrderBy(a => a.Start.Y).ThenBy(a => a.Start.X).ThenBy(a => a.Start.Z).ToList();
            Console.WriteLine($"time to sort {sw.Elapsed}");

            sw.Reset();

            service.Status("starting schematic import");

            sw.Start();
            foreach (var line in importLines)
            {
                service.Command($"fill?from={line.Start.X} {line.Start.Y} {line.Start.Z}&to={line.End.X} {line.End.Y} {line.End.Z}&tileName={line.BlockName}&tileData={line.Data}");
            }
            sw.Stop();
            service.Status($"time to queue commands {sw.Elapsed.TotalSeconds}");
            Console.WriteLine($"time to queue commands {sw.Elapsed.TotalSeconds}");
            sw.Reset();
            sw.Start();
            service.Wait();
            sw.Stop();
            service.Status($"time to complete import {sw.Elapsed.TotalSeconds}");
            Console.WriteLine($"time to complete import {sw.Elapsed.TotalSeconds}");
        }

        private static List<Line> ConvertFileToCommands(List<Point> points)
        {
            return LineFactory.CreateFromPoints(points);
        }
    }
}