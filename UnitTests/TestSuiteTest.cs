using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using OppaiSharp;
using SharpCompress.Common.Tar;
using SharpCompress.Readers;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests
{
	public class TestSuiteTest
	{
		private const string SuitePath = "test_suite_2019-02-19.tar.gz";
		private const string SuiteUrl = "http://github.com/Francesco149/oppai-ng/releases/download/2.3.2/" + SuitePath;
		private const string SuiteExpectedPath = "TestSuite.txt";

		private readonly List<Tuple<uint, ExpectedOutcome>> testCases = new List<Tuple<uint, ExpectedOutcome>>();
		private readonly ITestOutputHelper output;

		private const double error_margin = 0.06; // 6%

		public TestSuiteTest(ITestOutputHelper output)
		{
			this.output = output;

			ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

			//make sure that the results are downloaded
			if (!File.Exists(SuitePath))
				new WebClient().DownloadFile(SuiteUrl, SuitePath);

			//require the suite results to be here
			Skip.IfNot(File.Exists(SuiteExpectedPath), SuiteExpectedPath + " not found!");

			//load expected results from file
			testCases.Clear();
			using (var stream = File.OpenRead(SuiteExpectedPath))
			using (var sr = new StreamReader(stream))
			{
				while (!sr.EndOfStream)
				{
					string line = sr.ReadLine();
					if (string.IsNullOrEmpty(line)) continue;

					var c = new ExpectedOutcome(line, out uint id);
					testCases.Add(new Tuple<uint, ExpectedOutcome>(id, c));
				}
			}
		}

		[Fact]
		public void TestEntireSuite()
		{
			var swDecompression = new Stopwatch();
			var swParsing = new Stopwatch();
			var swCalculating = new Stopwatch();
			int beatmaps = 0;
			int totalCases = 0;
			double totalDiffSub100 = 0.0;
			double totalDiffSub200 = 0.0;
			double totalDiffSub300 = 0.0;
			double totalDiffOver300 = 0.0;

			using (var stream = new FileStream(SuitePath, FileMode.Open))
			using (var reader = ReaderFactory.Open(stream))
			{
				while (reader.MoveToNextEntry())
				{
					//only check actual files
					if (reader.Entry.IsDirectory || !(reader.Entry is TarEntry t)) continue;

					string fileName = t.Key; //eg: "test_suite/737100.osu"
					uint id = uint.Parse(fileName.Split('/').Last().Split('.').First());

					Beatmap bm;
					using (var ms = new MemoryStream())
					using (var str = new StreamReader(ms))
					{
						swDecompression.Start();
						reader.WriteEntryTo(ms);
						swDecompression.Stop();
						ms.Seek(0, SeekOrigin.Begin);

						++beatmaps;
						swParsing.Start();
						bm = Beatmap.Read(str);
						swParsing.Stop();

						foreach (var testcase in testCases.Where(a => a.Item1 == id && a.Item2.Mode != 1).Select(a => a.Item2))
						{
							var expected = testcase.PP;

							++totalCases;
							swCalculating.Start();
							var actual = CheckCase(bm, testcase);
							swCalculating.Stop();

							double margin = error_margin * expected;

							var diff = Math.Abs(actual - expected);

							if (expected < 100)
								totalDiffSub100 += diff;
							else if (expected < 200)
								totalDiffSub200 += diff;
							else if (expected < 300)
								totalDiffSub300 += diff;
							else
								totalDiffOver300 += diff;

							Assert.InRange(actual, expected - margin, expected + margin);
						}
					}
				}
			}

			var totalDiff = totalDiffSub100 + totalDiffSub200 + totalDiffSub300 + totalDiffOver300;
			var totalDiffWeighted =
				totalDiffSub100 / 3 + totalDiffSub200 / 2 + totalDiffSub300 / 1.5 + totalDiffOver300;
			output.WriteLine($"Test passed for {beatmaps} beatmaps with {totalCases} total cases");
			output.WriteLine($"Average PP difference: {totalDiff / totalCases:F3}");
			output.WriteLine($"Average PP difference (weighted): {totalDiffWeighted / totalCases:F3}");
			output.WriteLine("");
			output.WriteLine(
				$"Decompression time (avg/total): {TimeSpan.FromTicks(swDecompression.Elapsed.Ticks / beatmaps)} / {swDecompression.Elapsed}");
			output.WriteLine(
				$"Parsing time (avg/total):       {TimeSpan.FromTicks(swParsing.Elapsed.Ticks / beatmaps)} / {swParsing.Elapsed}");
			output.WriteLine(
				$"Calculating time (avg/total):   {TimeSpan.FromTicks(swCalculating.Elapsed.Ticks / totalCases)} / {swCalculating.Elapsed}");
			output.WriteLine(
				$"Total parsing + calculating time:                  {swParsing.Elapsed + swCalculating.Elapsed}");
		}

		[Fact]
		public void TestSinglePlay()
		{
			const int id = 774965;
			if (!File.Exists($"{id}.osu"))
				new WebClient().DownloadFile($"https://osu.ppy.sh/osu/{id}", $"{id}.osu");

			var reader = new StreamReader($"{id}.osu");

			//read a beatmap
			var beatmap = Beatmap.Read(reader);

			//calculate star ratings for HDDT
			Mods mods = Mods.Hidden | Mods.DoubleTime;
			var stars = new DiffCalc().Calc(beatmap, mods);
			output.WriteLine(
				$"Star rating: {stars.Total:F2} (aim stars: {stars.Aim:F2}, speed stars: {stars.Speed:F2})");

			//calculate the PP for this map
			//the play has no misses or 50's, so we don't specify it
			var pp = new PPv2(new PPv2Parameters(beatmap, stars, 99.55 / 100, mods: mods, combo: 1773));
			output.WriteLine($"Play is worth {pp.Total:F2}pp ({pp.Aim:F2} aim pp, {pp.Acc:F2} acc pp, {pp.Speed:F2} " +
			                 $"speed pp) and has an accuracy of {pp.ComputedAccuracy.Value() * 100:F2}%");

			Assert.InRange(813, pp.Total - 5, pp.Total + 5);
		}

		[Fact]
		public void TestManyHundreds()
		{
			const int id = 706711;
			if (!File.Exists($"{id}.osu"))
				new WebClient().DownloadFile($"https://osu.ppy.sh/osu/{id}", $"{id}.osu");

			var reader = new StreamReader($"{id}.osu");

			//read a beatmap
			var beatmap = Beatmap.Read(reader);
			var stars = new DiffCalc().Calc(beatmap);
			var pp = new PPv2(new PPv2Parameters(beatmap, stars, 1));

			//not checking the actual value, just making sure that it doesn't throw
		}

		private static double CheckCase(Beatmap bm, ExpectedOutcome outcome)
		{
			var pp = new PPv2(outcome.ToParameters(bm));
			return pp.Total;
		}

		private struct ExpectedOutcome
		{
			public readonly double PP;
			public readonly ushort Mode;

			private readonly ushort combo;
			private readonly ushort count300, count100, count50, countMiss;
			private readonly Mods mods;

			public ExpectedOutcome(string line, out uint id)
			{
				line = line.Trim(' ', ',', '{', '}');
				string[] s = line.Split(',');

				Skip.IfNot(s.Length == 9, "Invalid test case");

				Mode = ushort.Parse(s[0]);
				id = uint.Parse(s[1]);
				combo = ushort.Parse(s[2]);
				count300 = ushort.Parse(s[3]);
				count100 = ushort.Parse(s[4]);
				count50 = ushort.Parse(s[5]);
				countMiss = ushort.Parse(s[6]);

				PP = double.Parse(s[8], CultureInfo.InvariantCulture);

				string modString = s[7].Trim(' ').Replace(" | ", string.Empty).ToUpper();
				mods = Helpers.StringToMods(modString);
			}

			public PPv2Parameters ToParameters(Beatmap bm)
			{
				return new PPv2Parameters(bm, new Accuracy(count300, count100, count50, countMiss).Value(), countMiss,
					combo, mods);
			}
		}
	}
}
