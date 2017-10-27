using HumbleBundleBot;
using HumbleBundleServerless.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HumbleBundleServerless
{
    public static class ScheduledScraper
    {
        [FunctionName("ScheduledScraper")]
        public static void Run([TimerTrigger("0 0 */4 * * *")]TimerInfo myTimer,
            [Table("humbleBundles")] IQueryable<HumbleBundleEntity> currentBundles,
            [Table("humbleBundles")] ICollector<HumbleBundleEntity> bundlesTable,
            [Queue("bundlequeue")] ICollector<BundleQueue> bundleQueue,
            [Queue("updatequeue")] ICollector<string> updateQueue,
            TraceWriter log)
        {
            log.Info($"C# Timer trigger function executed at: {DateTime.Now}");

            var currentBundleNames = new List<String>();

            foreach (var bundle in currentBundles)
            {
                var fullBundle = bundle.GetBundle();
                log.Info($"Found stored bundle {fullBundle.Name}");
                currentBundleNames.Add(fullBundle.Name);
            }

            ScrapeAndCheckBundles(currentBundleNames, bundlesTable, bundleQueue, log, new HumbleScraper("https://www.humblebundle.com"), BundleTypes.GAMES);
            ScrapeAndCheckBundles(currentBundleNames, bundlesTable, bundleQueue, log, new HumbleScraper("https://www.humblebundle.com/books"), BundleTypes.BOOKS);
            ScrapeAndCheckBundles(currentBundleNames, bundlesTable, bundleQueue, log, new HumbleScraper("https://www.humblebundle.com/mobile"), BundleTypes.MOBILE);
            ScrapeAndCheckBundles(currentBundleNames, bundlesTable, bundleQueue, log, new HumbleScraper("https://www.humblebundle.com/software"), BundleTypes.SOFTWARE);
        }

        private static void ScrapeAndCheckBundles(List<String> currentBundleNames, ICollector<HumbleBundleEntity> bundlesTable, ICollector<BundleQueue> bundleQueue, TraceWriter log, HumbleScraper scraper, BundleTypes type)
        {
            var foundGames = scraper.Scrape();

            var bundles = GetBundlesFromItems(foundGames);

            foreach (var bundle in bundles)
            {
                log.Info($"Found current {type.ToString()} Bundle {bundle.Name} with {bundle.Sections.Sum(x => x.Games.Count)} items");

                if (!currentBundleNames.Any(x => x == bundle.Name))
                {
                    log.Info($"New bundle, adding to table storage");
                    bundlesTable.Add(new HumbleBundleEntity(type, bundle));
                    bundleQueue.Add(new BundleQueue()
                    {
                        Type = type,
                        Bundle = bundle
                    });
                }
            }
        }

        private static List<HumbleBundle> GetBundlesFromItems(List<HumbleItem> results)
        {
            var toReturn = new List<HumbleBundle>();

            foreach (var bundleResult in results.GroupBy(x => x.Bundle))
            {
                var bundle = new HumbleBundle
                {
                    Name = bundleResult.Key,
                    URL = bundleResult.First().URL,
                    Description = bundleResult.First().BundleDescription,
                    ImageUrl = bundleResult.First().BundleImageUrl
                };

                foreach (var section in bundleResult.GroupBy(x => x.Section))
                {
                    bundle.Sections.Add(new HumbleSection()
                    {
                        Title = section.Key,
                        Games = section.Select(x => x.Title).ToList()
                    });
                }

                toReturn.Add(bundle);
            }

            return toReturn;
        }
    }
}
