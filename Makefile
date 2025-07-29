publish:
	dotnet run --project src/RmqCli/RmqCli.csproj --no-launch-profile --no-build -- publish -q test-queue -m lul

release:
	dotnet publish src/RmqCli/RmqCli.csproj -c Release -r osx-arm64 -o release

clean:
	rm -rf release/*
