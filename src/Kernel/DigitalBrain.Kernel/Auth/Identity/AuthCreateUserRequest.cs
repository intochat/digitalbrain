using System.Security.Claims;
using DigitalBrain.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace DigitalBrain.Kernel;

internal sealed record AuthCreateUserRequest(string Username, string Password, string Role);

