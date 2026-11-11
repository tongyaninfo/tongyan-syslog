using System.ComponentModel.DataAnnotations;

namespace Syslog.Api.Models;

public sealed record LoginRequest([Required, MaxLength(64)] string Username, [Required, MaxLength(1024)] string Password);
