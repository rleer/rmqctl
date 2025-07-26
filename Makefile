publish:
	dotnet run --project src/rmqctl/rmqctl.csproj --no-launch-profile --no-build -- publish -q test-queue -m lul

release:
	dotnet publish src/rmqctl/rmqctl.csproj -c Release -r osx-arm64 -o release

clean:
	rm -rf release/*
