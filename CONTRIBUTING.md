# Contributing to Sitecore.API.Foundation.Authorization

We welcome contributions to the Sitecore.API.Foundation.Authorization project! This document provides guidelines for contributing to the project.

## How to Contribute

### Reporting Issues

1. **Search existing issues** first to avoid duplicates
2. **Use the issue templates** when creating new issues
3. **Provide detailed information** including:
   - Steps to reproduce
   - Expected vs actual behavior
   - Environment details (OS, etc.)
   - Code samples when applicable

### Submitting Pull Requests

1. **Fork the repository** and create a feature branch
2. **Follow the coding standards** outlined below
3. **Write tests** for new functionality
4. **Update documentation** as needed
5. **Ensure all tests pass** before submitting
6. **Create a clear PR description** explaining the changes

## Development Setup

### Prerequisites

- .NET 9.0 SDK
- Visual Studio 2022 17.8+ or VS Code with C# extension
- Git
- Docker Desktop (for integration tests)

### Getting Started

```bash
# Clone the repository
git clone https://github.com/your-username/Sitecore.API.Foundation.Authorization.git
cd Sitecore.API.Foundation.Authorization

# Restore packages
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Project Structure

```
Sitecore.API.Foundation.Authorization/
src/
   Sitecore.API.Foundation.Authorization/    # Main library
tests/
   Sitecore.API.Foundation.Tests/            # Unit tests
   Sitecore.API.Foundation.Authorization.IntegrationTests/  # Integration tests
docs/                                          # Documentation
.github/                                       # GitHub workflows
README.md
```

## Coding Standards

### General Guidelines

- Follow [Microsoft's C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful names for variables, methods, and classes
- Write self-documenting code with clear intent
- Add XML documentation for public APIs
- Keep methods focused and concise

### Code Style

- **Indentation**: 4 spaces (no tabs)
- **Line endings**: LF (Unix-style)
- **File encoding**: UTF-8
- **Maximum line length**: 120 characters

### Naming Conventions

- **Classes**: PascalCase (`SitecoreTokenService`)
- **Methods**: PascalCase (`GetSitecoreAuthToken`)
- **Properties**: PascalCase (`AccessToken`)
- **Fields**: camelCase with underscore prefix (`_tokenCache`)
- **Parameters**: camelCase (`clientCredentials`)
- **Constants**: PascalCase (`DefaultMaxCacheSize`)

### Code Organization

- **Namespaces**: Follow folder structure
- **Using statements**: Order alphabetically, system first
- **Access modifiers**: Always specify explicitly
- **Async methods**: Suffix with `Async`

## Testing Guidelines

### Unit Tests

- **Test naming**: `Should_ExpectedBehavior_When_StateUnderTest`
- **Arrange-Act-Assert** pattern
- **One assertion per test** when possible
- **Test edge cases** and error conditions
- **Use mocking** for external dependencies

### Integration Tests

- **Test real scenarios** end-to-end
- **Use Docker containers** for external services
- **Clean up resources** properly
- **Document test setup** requirements

### Test Coverage

- Aim for **80%+ code coverage**
- Focus on **critical paths** and **business logic**
- Include **positive and negative test cases**
- Test **concurrent scenarios** for thread-safe code

## Pull Request Process

### Before Submitting

1. **Rebase your branch** against the latest main
2. **Run all tests** and ensure they pass
3. **Check code coverage** hasn't decreased significantly
4. **Update documentation** if needed
5. **Document significant changes in your PR description**

### PR Requirements

- **Clear title** describing the change
- **Detailed description** explaining what and why
- **Link to related issues** using keywords (fixes #123)
- **Screenshots or examples** for UI changes
- **Breaking change notes** if applicable

### Review Process

1. **Automated checks** must pass (CI/CD)
2. **Code review** by project maintainers
3. **Testing** on different environments
4. **Documentation review** if applicable
5. **Final approval** and merge

## Release Process

Releases are published via CI/CD after PRs are merged.

## Issue Labels

- `bug`: Something isn't working
- `enhancement`: New feature or request
- `documentation`: Improvements or additions to docs
- `good first issue`: Good for newcomers
- `help wanted`: Extra attention is needed
- `question`: Further information is requested
- `wontfix`: This will not be worked on

## Communication

### Channels

- **GitHub Issues**: Bug reports, feature requests
- **GitHub Discussions**: General questions, ideas
- **Pull Requests**: Code contributions

### Guidelines

- **Be respectful** and constructive
- **Search before posting** to avoid duplicates
- **Provide context** and examples
- **Follow up** on your contributions

## Resources

### Documentation

- [README.md](README.md) - Project overview and usage
- [CONTRIBUTING.md](CONTRIBUTING.md) - Development and contribution guidelines (this file)
- Source code with XML documentation comments

### Learning Resources

- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [OAuth2 Specification](https://tools.ietf.org/html/rfc6749)
- [xUnit Testing](https://xunit.net/)
- [Docker Documentation](https://docs.docker.com/)

## Recognition

Contributors will be recognized in:

- **CONTRIBUTORS.md** file
Significant contributions will be recognized in project documentation.
- **GitHub contributors** list

Thank you for contributing to Sitecore.API.Foundation.Authorization!