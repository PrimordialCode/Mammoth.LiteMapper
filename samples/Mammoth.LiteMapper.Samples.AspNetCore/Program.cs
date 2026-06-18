using System;
using Mammoth.LiteMapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mammoth.LiteMapper.Samples.AspNetCore
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 1 && string.Equals(args[0], "--smoke", StringComparison.Ordinal))
            {
                var dto = UserMapper.Map(new User { Id = 5, Name = "Grace" });
                if (dto.Id != 5 || dto.Name != "Grace")
                {
                    throw new InvalidOperationException("ASP.NET Core sample mapping failed.");
                }

                return;
            }

            var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/user", static context =>
            {
                var dto = UserMapper.Map(new User { Id = 5, Name = "Grace" });
                return context.Response.WriteAsJsonAsync(dto);
            });
            app.Run();
        }
    }

    [LiteMapper]
    public static partial class UserMapper
    {
        public static partial UserDto Map(User source);
    }

    public sealed class User
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    public sealed class UserDto
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
