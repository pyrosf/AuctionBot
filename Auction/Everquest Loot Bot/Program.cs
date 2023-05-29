using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoHotkey.Interop;



class Program
{
    static string _filePath = @"C:\Users\Public\Daybreak Game Company\Installed Games\EverQuest\Logs\eqlog_Glass_oakwynd.txt";
    static long _fileSize = 0;
    static bool firsttime = true;
    static ConcurrentQueue<string> _lineQueue = new ConcurrentQueue<string>();

    static void PrintNewLines()
    {
        // Open the log file
        using (var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            // Set the file position to the end of the previous file contents
            fileStream.Seek(_fileSize, SeekOrigin.Begin);

            // Read the new lines that were added to the file
            using (var streamReader = new StreamReader(fileStream))
            {
                string line;
                while ((line = streamReader.ReadLine()) != null)
                {

                     
                    if (firsttime)
                    { continue; }
                    _lineQueue.Enqueue(line);  // this prevents locking and ensures each line is put in the queue to be processed without impacting the file
                    Console.WriteLine(line);
                }

                // Update the file size to the current position in the file
                _fileSize = fileStream.Position;
                firsttime = false;
            }
        }
    }

    static void Main()
    {
        Thread DumpThread = new Thread(() =>
        {
            string channel = "/rs";  // raid say  // Changed to just use default channel
            var ahk2 = AutoHotkeyEngine.Instance;



            while (true)
            {
                Thread.Sleep(5200);
                Console.WriteLine("test");
                ahk2.ExecRaw("SendEvent,/outputfile raid");
                Thread.Sleep(200);
                ahk2.ExecRaw("SendEvent,{Enter}");
                Thread.Sleep(2000);
                ahk2.ExecRaw("SendEvent,/rs ~~~~Dump Taken~~~~");
                Thread.Sleep(200);
                ahk2.ExecRaw("SendEvent,{Enter}");
                Thread.Sleep(30 * 60 * 1000); // Sleep for 30 minutes



            }

        });

            // setting up AutoHotKey
            var ahk = AutoHotkeyEngine.Instance;
        ahk.ExecRaw("SetKeyDelay, 2");
        bool stopPrinting = false;
        char DelChar = ' ';

        Thread printThread = new Thread(() =>
        {
            while (!stopPrinting)
            {
                foreach (string Qline in _lineQueue)
                {
                    string queueline = "";
                    bool isRemoved = _lineQueue.TryDequeue(out queueline);
                    string line = Qline.Substring(27);
                    //[Fri May 19 09:24:24 2023] Mkorisu tells the raid, '!Auction Shiny Thing'
                    //[Fri May 19 09:24:24 2023] Mkorisu tells the raid, '1'
                    if (line.Contains("!Auction"))
                    {
                        
                        string[] linesegments = line.Split(DelChar);
                        string JoinedArray = string.Join(" ", linesegments.Skip(5));
                        JoinedArray = JoinedArray.Remove(JoinedArray.Length - 1);


                        Auction auction = new Auction(JoinedArray);
                        Thread AuctionThread = new Thread(auction.Start);
                        AuctionThread.Start();

                        while (AuctionThread.IsAlive)
                        {
                            foreach (string Qline2 in _lineQueue)
                            {
                                if (Qline2.Contains("tells the raid,"))
                                {
                                    Console.WriteLine(Qline2);

                                    bool isRemoved2 = _lineQueue.TryDequeue(out queueline);
                                    string line2 = Qline2.Substring(27);
                                    line2 = line2.Replace("'", "");
                                    string[] LineSegmentsAuction = line2.Split(DelChar);
                                    auction.ProcessBid(LineSegmentsAuction[0] + " bids " + LineSegmentsAuction[4]);
                                    auction.bidReceivedEvent.Set();
                                }



                            }


                        }


                    }



                }
            }
        });
        Thread ReadThread = new Thread(() =>
        {
            while (!stopPrinting)
            {
                // Print out the current contents of the file
                PrintNewLines();


                // Create a new FileSystemWatcher object to monitor the log file
                using (var watcher = new FileSystemWatcher(Path.GetDirectoryName(_filePath), Path.GetFileName(_filePath)))
                {
                    // Set the notification filter to watch for changes in LastWrite time
                    watcher.NotifyFilter = NotifyFilters.LastWrite;

                    // Start watching for changes
                    watcher.EnableRaisingEvents = true;

                    // Loop indefinitely
                    while (true)
                    {
                        // Wait for a change notification
                        var result = watcher.WaitForChanged(WatcherChangeTypes.Changed);

                        // Check if the file was changed
                        if (result.ChangeType == WatcherChangeTypes.Changed)
                        {
                            // Print out the new lines that were added to the file
                            PrintNewLines();
                        }
                    }
                }

            }

        });

        printThread.Start();
        ReadThread.Start();
        DumpThread.Start();
        // Wait for the user to press Enter to stop printing
        Console.ReadLine();

        stopPrinting = true; // Set the flag to stop the printing loop
        printThread.Join(); // Wait for the printThread to finish
        ReadThread.Join();
        Console.WriteLine("Printing stopped.");
    }
    class Auction
    {
        private Dictionary<string, int> bids;
        private string topBidder;
        private int topBid;
        private string item;
        private bool auctionEnded;
        private bool goingOnce;
        private bool goingTwice;
        public ManualResetEvent bidReceivedEvent;



        public Auction(string itemName)
        {
            bids = new Dictionary<string, int>();
            topBidder = null;
            topBid = 0;
            item = itemName;
            auctionEnded = false;
            goingOnce = false;
            goingTwice = false;
            bidReceivedEvent = new ManualResetEvent(false);
        }
        private void WriteWinningBidToFile()
        {
            string filePath = "C:\\temp\\EQTestFiles\\winning_bid.txt";
            string winningBid = $"{item} ; {topBid} ; {topBidder} Gratss \r\n";

            try
            {
                File.AppendAllText(filePath, winningBid);
                Console.WriteLine("Winning bid has been written to file: " + filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing winning bid to file: " + ex.Message);
            }
        }
        public void Start()
        {
            string channel = "/rs ";
            var ahk = AutoHotkeyEngine.Instance;

            ahk.ExecRaw("SendEvent, " + channel + "~~~~Auction started for item: " + item + "~~~~");
            Thread.Sleep(200);
            ahk.ExecRaw("SendEvent,{Enter}");

            Thread timerThread = new Thread(TimerThread);
            timerThread.Start();

            while (!auctionEnded)
            {
                //string input = Console.ReadLine();
                //ProcessBid(input);
                //bidReceivedEvent.Set(); // Signal that a bid has been received
            }


            ahk.ExecRaw("SendEvent " + channel + "Auction ended.   Winner: " + topBidder + " With bid of: " + topBid);
            Thread.Sleep(200);
            ahk.ExecRaw("SendEvent,{Enter}");

        }

        public void ProcessBid(string bid)
        {
            if (bid.Contains("bids"))
            {
                string[] parts = bid.Split(' ');
                if (parts.Length == 3)
                {
                    string bidder = parts[0];
                    int amount;
                    if (int.TryParse(parts[2], out amount))
                    {
                        if (amount > topBid)
                        {
                            topBidder = bidder;
                            topBid = amount;
                            Console.WriteLine($"New Top Bidder {topBidder} with {topBid}");

                        }

                        if (!bids.ContainsKey(bidder))
                        {
                            bids.Add(bidder, amount);
                        }
                        else
                        {
                            bids[bidder] = amount;
                        }

                        // Reset countdown if bid is received during "going once" period
                        if (goingOnce)
                        {
                            goingOnce = false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Invalid bid amount.");
                    }
                }
                else
                {
                    Console.WriteLine("Invalid bid format.");
                }
            }
        }

        private void DumpThread()
        {
            string channel = "/rs";  // raid say  // Changed to just use default channel
            var ahk = AutoHotkeyEngine.Instance;



            while (true)
            {
                Thread.Sleep(5000);
                Console.WriteLine("test");
                ahk.ExecRaw("SendEvent,/outputfile raid");
                Thread.Sleep(200);
                ahk.ExecRaw("SendEvent,{Enter}");
                Thread.Sleep(5000);
                ahk.ExecRaw("SendEvent,/rs Dump Taken");
                Thread.Sleep(200);
                ahk.ExecRaw("SendEvent,{Enter}");
                Thread.Sleep(30 * 60 * 1000); // Sleep for 30 minutes
                


            }

        }

            private void TimerThread()
        {
            string channel = "/rs";  // raid say  // Changed to just use default channel
            var ahk = AutoHotkeyEngine.Instance;
            Thread.Sleep(15000); // Wait for 25 seconds  // dropped to 10 sec
            Console.WriteLine("Current top bidder at the 5 second mark: " + topBidder + " Bidding:" + topBid);
            ahk.ExecRaw("SendEvent, "+ channel + " Current top bidder with 10 seconds   : " + topBidder + " Bidding: " + topBid);
            Thread.Sleep(200);
            ahk.ExecRaw("SendEvent,{Enter}");
            Thread.Sleep(5000); // Wait for additional 5 seconds

            if (!goingOnce)
            {
                goingOnce = true;
                Console.WriteLine("Going once...");
                ahk.ExecRaw("SendEvent, " + channel + " Going once... ");
                Thread.Sleep(200);
                ahk.ExecRaw("SendEvent,{Enter}");
            }

            Thread.Sleep(2000); // Wait for 2 seconds

            if (!goingTwice && goingOnce)
            {
                goingTwice = true;
                Console.WriteLine("Going twice...");
                ahk.ExecRaw("SendEvent, " + channel + " Going twice... ");
                Thread.Sleep(200);
                ahk.ExecRaw("SendEvent,{Enter}");
            }

            Thread.Sleep(2000); // Wait for 2 seconds

            if (goingTwice && goingOnce)
            {
                
                Console.WriteLine("Sold!");
                ahk.ExecRaw("SendEvent, " + channel + " Sold to " + topBidder + " for: " + topBid);
                Thread.Sleep(200);
                ahk.ExecRaw("SendEvent,{Enter}");
                WriteWinningBidToFile();
                auctionEnded = true;

            }
            else
            {
                goingOnce = false;
                goingTwice = false;

                // Wait for a bid or 2 seconds, whichever occurs first
                if (bidReceivedEvent.WaitOne(2000))
                {
                    bidReceivedEvent.Reset();
                    TimerThread(); // Reset the countdown
                }
                else
                {
                    auctionEnded = true;
                    Console.WriteLine("No more bids. Auction ended.");
                }
            }
        }
    }
}
