using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace LotusSim
{
    class Program
    {
        private static SerialPort port;
        private static string path;
        private static int delay;

        static void Main(string[] args)
        {
            if (args.Count() != 1)
            {
                Console.WriteLine("Please pass a file as argument.");
                return;
            }
            else if (!File.Exists(args[0]))
            {
                Console.WriteLine("File not found.");
            }
            else
            {
                path = args[0];
            }
            Console.Write("Enter a delay in ms between frames (or leave empty for realtime): ");
            string _val = "";
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(true);
                if (key.Key != ConsoleKey.Backspace)
                {
                    double val = 0;
                    bool _x = double.TryParse(key.KeyChar.ToString(), out val);
                    if (_x)
                    {
                        _val += key.KeyChar;
                        Console.Write(key.KeyChar);
                    }
                }
                else
                {
                    if (key.Key == ConsoleKey.Backspace && _val.Length > 0)
                    {
                        _val = _val.Substring(0, (_val.Length - 1));
                        Console.Write("\b \b");
                    }
                }
            }
            while (key.Key != ConsoleKey.Enter);
            if (_val == null || _val == String.Empty)
            {
                delay = -1;
                Console.WriteLine("realtime");
            }
            else
            {
                delay = int.Parse(_val);
                Console.WriteLine();
            }
            while (port == null)
            {
                SelectPort();
            }
            while (true)
            {
                SendFile();
                Console.WriteLine("Done. Repeat? [y/n]");
            RepeatQuestion:
                switch (Console.ReadKey(true).KeyChar)
                {
                    case 'y':
                    case 'Y':
                        break;
                    case 'n':
                    case 'N':
                        return;
                    default:
                        Console.WriteLine();
                        goto RepeatQuestion;
                }
            }
        }

        static void SelectPort()
        {
            var availablePorts = SerialPort.GetPortNames();
            if (availablePorts.Count() == 0)
            {
                Console.WriteLine("No serial ports were found. Press any key to try again...");
                Console.ReadKey(true);
                Console.WriteLine();
                return;
            }
            Console.WriteLine("Available ports:");
            foreach (var availablePort in availablePorts)
            {
                Console.WriteLine(availablePort);
            }
            Console.Write("Select port: ");
            var chosenPort = Console.ReadLine();
            try
            {
                port = new SerialPort(chosenPort, 57600, Parity.None, 8, StopBits.One);
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Please enter a port name.\n");
                return;
            }
            catch (IOException)
            {
                if (!availablePorts.Contains(chosenPort))
                {
                    Console.WriteLine("The specified port could not be found.\n");
                }
                else
                {
                    Console.WriteLine("The specified port could not be openend.\n");
                }
                return;
            }
            try
            {
                port.Open();
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Port access denied.\n");
                port = null;
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Invalid port name.\n");
                port = null;
            }
            catch (IOException)
            {
                Console.WriteLine("The specified port could not be found.\n");
                port = null;
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("The port is already open.\n");
            }
        }

        static void SendFile()
        {
            Console.WriteLine("Sending...");
            switch (Path.GetExtension(path))
            {
                case ".txt":
                    SendFromText();
                    break;
                case ".bin":
                    SendFromBinary();
                    break;
                default:
                    Console.WriteLine("Filetype not supported.");
                    break;
            }
        }
        static void SendFromText()
        {
            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    string line = String.Empty;
                    var lineNr = 0;
                    uint prevTimestamp = 0;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lineNr++;
                        if (line.Length % 2 != 0)
                        {
                            Console.WriteLine($"Error on line {lineNr}.");
                            return;
                        }
                        var data = new byte[line.Length / 2];
                        for (int i = 0; i < line.Length; i += 2)
                        {
                            try
                            {
                                data[i / 2] = Convert.ToByte(line.Substring(i, 2), 16);
                            }
                            catch (Exception)
                            {
                                Console.WriteLine($"Error on line {lineNr} col {i}.");
                                return;
                            }
                        }
                        SendData(data, ref prevTimestamp);
                    };
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("File not found.");
            }
        }

        static void SendFromBinary()
        {
            using (var stream = File.OpenRead(path))
            {
                uint prevTimestamp = 0;
                while (true)
                {
                    var signature = new byte[2];
                    if (stream.Read(signature, 0, 2) != 2)
                        break;
                    if ((signature[0] & 0xFC) != 0xAC)
                    {
                        port.Write(signature, 0, 2);
                        continue;
                    }
                    var lengthArr = new byte[2] { (byte) (signature[0] & 0x03), signature[1] };
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(lengthArr);
                    var length = BitConverter.ToUInt16(lengthArr, 0);
                    var data = new byte[2+length];
                    if (stream.Read(data, 2, length) != length)
                        continue;
                    Array.Copy(signature, 0, data, 0, 2);
                    SendData(data, ref prevTimestamp);
                }
            }
        }

        static void SendData(byte[] data, ref uint prevTimestamp)
        {
            var timestamp = new byte[4];
            Array.Copy(data, 5, timestamp, 1, 3);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(timestamp);
            var currTimestamp = BitConverter.ToUInt32(timestamp, 0);
            try
            {
                port.Write(data, 0, data.Length);
                var delay = (int)(currTimestamp - prevTimestamp) * 10;
                Thread.Sleep(Program.delay >= 0 ? Program.delay : (delay < 500 && delay > 0 ? delay : 500));
                prevTimestamp = currTimestamp;
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Port is closed.");
                return;
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Connection timed out.");
                return;
            }
        }
    }
}
