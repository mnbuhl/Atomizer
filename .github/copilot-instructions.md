# GitHub Copilot Custom Instructions for Atomizer

## Atomizer Project Overview
Atomizer is a lightweight, extensible job scheduling & queueing framework for ASP.NET Core, designed for high throughput and low friction in modern distributed applications.
It supports multiple storage backends, graceful shutdowns, all while being easy to extend.
---

## Project Standards
- Target `.netstandard 2.1` for the main Atomizer library.
- Use modern C# syntax (.NET 6+) for EF Core and related libraries.
- Prefer interfaces for abstractions; keep implementations separate.
- Adhere to SOLID principles and ensure code is testable.
- Add XML documentation to all public APIs and extension points.
- Use `System.Text.Json` for JSON serialization/deserialization.
- Use `Microsoft.Extensions.DependencyInjection` for dependency injection.
- Use `Microsoft.Extensions.Logging` for logging.
- Adhere to csharpier formatting standards.

## Testing Practices
- Use XUnit for all unit and integration tests.
- Mock dependencies with NSubstitute.
- Use Testcontainers for integration tests needing external services.
- Use AwesomeAssertions for expressive assertions.
- Place all tests in the `tests/` folder, mirroring the source structure.
- Name test classes `{Type}Tests` and methods `{Method}_When{Scenario}_Should{ExpectedBehavior}`.

## Copilot Usage
- Match the existing code style and architecture.
- When generating tests, use the specified libraries.
- Preserve public APIs when refactoring unless otherwise instructed.
- Always provide XML documentation for public members.

---
*Update these instructions as the project evolves.*