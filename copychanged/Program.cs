using System;
using System.IO;

namespace copychanged
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                string[] files = Directory.GetFiles(@"Z:\tmp\testtesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttest\testtesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttest\testtesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttest\testtesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttesttest\");
                Console.WriteLine();
                if(files.Length == 0)
                {
                    return;
                }
                string file = files[0];
                string ext = Path.GetExtension(file);
                if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(file, Path.ChangeExtension(file, ".test"));
                    Console.WriteLine($"Moved {file} to .test");
                }
                else if(ext.Equals(".test", StringComparison.OrdinalIgnoreCase))
                {
                    File.Move(file, Path.ChangeExtension(file, ".txt"));
                    Console.WriteLine($"Moved {file} to .txt");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.WriteLine("Hello World!");
            Console.ReadKey();
        }
    }
}
