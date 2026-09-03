## Known Issues

### Unrelated formatting changes in `.csproj`/`.props` files

When a `.csproj` or `.props` file is updated, every self-closing XML element in that file (e.g. `<Using Include="Foo"/>`) may be rewritten with a space before the closing slash (`<Using Include="Foo" />`), even for elements UpdatR didn't otherwise touch. This is caused by `System.Xml.XmlDocument`, which is used to parse and rewrite these files: saving an `XmlDocument` always re-serializes the *entire* document using .NET's default formatting for empty elements, and there's no `XmlWriterSettings` to preserve the original file's self-closing style per element.

This won't be fixed, since avoiding it would require replacing the `XmlDocument`-based read/write with a fully text-based, surgical rewrite (similar to how UpdatR already handles `#:package` directives in file-based apps) - a significant rewrite for a purely cosmetic issue. `dotnet-tools.json` and file-based apps aren't affected, since they're already updated by rewriting only the exact text that changed.
