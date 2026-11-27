# Third-party notices

Visual Studio Translator is licensed under the [GNU General Public License version 3 only](LICENSE), with an
additional permission described in [NOTICE.md](NOTICE.md). It includes, or downloads when it is first used, the
components below, each under its own license.

## Included in the extension

The Engine, the process that translates, is published self-contained and so carries these with it.

| Component | License |
|---|---|
| [.NET runtime](https://github.com/dotnet/runtime) | MIT |
| [Microsoft.Extensions.Hosting](https://github.com/dotnet/runtime) and the Microsoft.Extensions libraries it uses | MIT |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | MIT |
| [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | Apache License 2.0 |
| [SQLite](https://sqlite.org) | Public domain |
| [StreamJsonRpc](https://github.com/microsoft/vs-streamjsonrpc) | MIT |
| [Nerdbank.Streams](https://github.com/dotnet/Nerdbank.Streams) | MIT |
| [Microsoft.VisualStudio.Threading](https://github.com/microsoft/vs-threading) | MIT |
| [Microsoft.VisualStudio.Validation](https://github.com/microsoft/vs-validation) | MIT |
| [MessagePack for C#](https://github.com/MessagePack-CSharp/MessagePack-CSharp) | MIT |
| [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) | MIT |

## Downloaded on first use, after the user agrees

| Component | Source | License |
|---|---|---|
| [llama.cpp](https://github.com/ggml-org/llama.cpp), build b12317 for Windows x64 with Vulkan | GitHub releases of ggml-org/llama.cpp | MIT |
| [Gemma 4 E4B](https://huggingface.co/google/gemma-4-E4B-it) or [Gemma 4 E2B](https://huggingface.co/google/gemma-4-E2B-it) by Google DeepMind, as 4-bit GGUF files quantized by mradermacher | Hugging Face | Apache License 2.0 |

The files are checked against their SHA-256 hashes before they are used.

## Used only to build

[PolySharp](https://github.com/Sergio0694/PolySharp) (MIT) generates source code at build time and ships nothing of
its own. The test projects use [xUnit](https://github.com/xunit/xunit) (Apache License 2.0) and
[AwesomeAssertions](https://github.com/AwesomeAssertions/AwesomeAssertions) (Apache License 2.0); they are not part
of the extension.

The full texts of the licenses are at the projects' addresses above.