# Change Log

## v0.2.9

* Implement performance-oriented logging and use the results to improve performance of task element completions.
* Switch Verbose and Debug logging levels (in my opinion, Serilog has these backwards).

## v0.2.8

* _Experimental:_ Add completions for task elements based on task types declared in the project.
* _Experimental:_ Add completions for task attributes based on task types declared in the project.
* More testing on MacOS and Linux.
* _Experimental:_ Parsing of MSBuild item transform expressions.
* _Experimental:_ Add experimental feature flag (`empty-completion-lists`) to enable returning empty completion lists rather than null  
  Fixes tintoy/msbuild-project-tools-vscode#17.  
  We can't do this by default because our extension depends on VSCode's behaviour when null is returned vs an empty completion list (when null is returned, no completion list is displayed; when an empty completion list is returned, purely-textual completions are displayed based on current file contents).  
  This behaviour is mainly to support clients other than VSCode (e.g. aCute).

## v0.2.7

* Add setting to control which types of objects from the current projects are included when offering completions.
* _Experimental:_ Add completions for qualified and unqualified item metadata expressions (`%(XXX.YYY)` and `%(YYY)`).

## v0.2.6

* Bug-fix: attribute completions should be available on elements that don't currently have any attributes.
* Bug-fix: go-to-definition should also work for regular-style project imports (not just SDK-style imports).
* _Experimental:_ Add completions for MSBuild property and item expressions (`$(XXX)` and `@(XXX)`).

## v0.2.4

* Bug-fix: missing completions for top-level elements (e.g. `<PropertyGroup>`, `<ItemGroup>`, `<Target>`).
* Improve help for well-known items and their metadata.
* Bug-fix for tintoy/msbuild-project-tools-vscode#11 (should not fail on non-standard file extension).

## v0.2.3

* Add help for well-known elements, attributes, properties, and item types from `MSBuild.*.xsd` to improve completions and tooltips-on-hover.
* Improve completions for attributes that refer to target names.

## v0.2.2

* Add completions for attributes that refer to target names.

## v0.2.1

* Add completions for top-level elements (e.g. `<PropertyGroup>`, `<ItemGroup>`, `<Target>`).
* Add completions for property elements (both common and locally-defined).
* Improve language-service internals (more consistently accurate comprehension of project contents).

## v0.2.0

* Improved completions:
  * Add completions for `PackageReference` and `DotNetCliToolReference`.
  * Add completions for common item attributes.
  * Add completions for property `Condition` elements.
  * Support for logging to [Seq](https://getseq.net/).  
    Only useful if you're hacking on the language service itself.

## v0.1.12

* Sort package versions in descending order for classic completion provider, too.  
  If you prefer the old behaviour, you can set `msbuildProjectTools.nuget.newestVersionsFirst` to `false`.

## v0.1.11

* Sort package versions in descending order.  
  If you prefer the old behaviour, you can set `msbuildProjectTools.nuget.newestVersionsFirst` to `false`.

## v0.1.10

* Improve tooltip content when hovering on MSBuild XML.
* Enable jumping from PackageReference element to package on NuGet.org.

## v0.1.9

* Add specific hover tooltip for Condition attributes.

## v0.1.8

* Add basic syntax-highlighting for expressions in MSBuild projects (currently only supported in attribute values).
* Improve delay on first completion of PackageReference by asynchronously warming up the NuGet client.

## v0.1.7

* Add configuration setting to disable tooltip-on-hover.
* Add configuration setting to control logging verbosity.

## v0.1.6

* Actually enable the language server by default (sorry about that).

## v0.1.5

* Language server is now enabled by default.
* Improve calculation of line / column offsets.

## v0.1.4

* Provide intellisense for regular-style and SDK-style imports whose conditions evaluate to false.
* Respect the user's nominated version of the .NET Core tooling to use when loading master projects (equivalent to running `dotnet --version` in the solution directory, this respects `global.json` if present).

## v0.1.3

* Provide intellisense for items whose conditions evaluate to `false`.
* Show information about conditions on hover for items and properties.

## v0.1.2

* Handle `Import` elements that give rise to multiple imported projects (this already worked correctly for SDK-style imports).
* Initial support for master and sub projects.

## v0.1.1

* Use a patched version of `Microsoft.Language.Xml` that behaves correctly in non-windows environments (issues with CR vs CRLF line-endings).
* Improve tooltips on hover.

## v0.1.0

* Fix handling of non-windows line endings.

## v0.1.0-rc1

* Fix cross-platform path handling.

## v0.1.0-beta2

* More informative tooltips on hover
* Handle multiple items originating from a single item group element in the XML.
* Improved error handling

## v0.1.0-beta1

* Implement go-to-definition for project-style and SDK-style imports.
* Detect .NET Core version on startup, and fall back to classic completion provider if >= 2.0.0 is not available.

## v0.1.0-alpha2

* Add configuration property (`msbuildProjectFileTools.languageService.enable`) to switch between MSBuild language engine and classic completion provider.

## v0.1.0-alpha1

* The extension now uses a language server based on Microsoft's Language Server Protocol.
* Tooltips for MSBuild objects in the project file.
* Support for any configured (remote) package source.  
  We're using the new NuGet client libraries so it should understand global, solution-local, and project-local package sources.
* Highly-improved handling of project files with broken or invalid XML (thanks to `Microsoft.Language.Xml`).

## v0.0.2

* Resolve the URL for the NuGet v3 AutoComplete API at extension startup.

## v0.0.1

* Initial release.
