# Offline storage verification

Run from the bijiSDK project root:

```powershell
dotnet run --project verification/storage/harness/StorageTests.csproj --verbosity quiet
```

The standalone .NET 9 harness compiles the actual RecoverableFileConfigStorage.cs and
uses minimal interface stubs. It verifies Windows filesystem behavior only, not Unity
compilation or Android/iOS behavior. The integrated core tests remain a separate check.

Twelve checks cover fresh storage, current/backup replacement, uncommitted-temp exclusion,
corrupt-current backup recovery, replacement after corruption, locked-file failure and
preservation, deletion markers, write-after-delete, invalid keys/slots, and slot isolation.

Each run writes its test data under harness/runs. previous-run contains the original
successful run's data, retained when the temporary harness was moved into this project.