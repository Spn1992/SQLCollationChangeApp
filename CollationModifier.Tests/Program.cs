using System;

namespace CollationModifier.Tests
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                MainFormTests.RunAllTests();
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TEST FAILURE: {ex}");
                return 1;
            }
        }
    }
}
