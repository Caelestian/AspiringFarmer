# Compiler checks

These fixtures compile against the library projects as an external consumer.
The default `Valid` fixture also participates in ordinary solution builds.
The runner builds each fixture separately and checks its exit status and expected diagnostic.

From the repository root:

```sh
dotnet restore AspiringFarmer.slnx
python3 tests/Compiler/check.py
```

The runner requires Python 3 and a .NET 10 SDK. Run it separately from solution builds because the fixtures share build outputs.

| Fixture | Expected result |
| --- | --- |
| `Valid` | Compiles typed descriptions, inherited native callbacks, a connection resource factory, implicit yields, conditionals, and loops |
| `InvalidVolume` | Rejects `data_volume` inside a project expression |
| `InvalidWait` | Rejects waiting on a plain parameter without `IResourceWithWaitSupport` |
| `InvalidReference` | Rejects a plain parameter without connection-string or project service-discovery support |
| `InvalidCallback` | Rejects a project callback inside a Redis expression |

Unknown CE operations currently produce FS0708 about a missing CE translation member.
The volume fixture records that diagnostic rather than claiming the error names the missing operation.
Improving these diagnostics remains API design work.

Native model and registry checks remain in the runnable AppHost:

```sh
dotnet build prototypes/NativeAspire
dotnet prototypes/NativeAspire/bin/Debug/net10.0/NativeAspire.dll --verify
```
