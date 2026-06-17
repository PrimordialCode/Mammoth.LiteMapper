namespace Mammoth.LiteMapper.Samples.AspNetCore
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.Run();
        }
    }
}
