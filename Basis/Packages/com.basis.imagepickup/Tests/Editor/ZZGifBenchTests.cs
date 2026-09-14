using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Basis.ImagePickup.Tests
{
    public class ZZGifBenchTests
    {
        private const string Folder = @"C:\Users\doola\OneDrive\Pictures\Basis\";
        private const string ReportPath = @"C:\Users\doola\AppData\Local\Temp\claude\C--Users-doola\b388aef1-b631-4110-b38c-d9dcff4b42f9\scratchpad\bench\gifbench_report.txt";

        [Test]
        public void MeasureGifPipelineAgainstHead()
        {
            if (Environment.GetEnvironmentVariable("BASIS_GIF_BENCH") != "1")
                Assert.Ignore("Set BASIS_GIF_BENCH=1 to run the GIF benchmark.");
            string[] names =
            {
                "Gif_20260824_194458_960x540.gif",
                "Gif_20260823_061748_480x270.gif",
                "Gif_20260814_084242_960x540.gif",
                "Gif_20260912_174626_480x270.gif",
            };
            var paths = new List<string>();
            foreach (string name in names)
            {
                if (File.Exists(Folder + name))
                    paths.Add(Folder + name);
            }
            if (paths.Count == 0)
                Assert.Ignore("No sample GIFs.");
            string report = ZZGifBench.Run(paths, 5);
            File.WriteAllText(ReportPath, report);
            Debug.Log("[GIFBENCH]\n" + report);
        }
    }
}
