set CGO_ENABLED=0

set GOOS=windows
set GOARCH=amd64
go build -o nori-api-windows.exe ./cmd/server

set GOOS=linux
set GOARCH=amd64
go build -o nori-api-linux ./cmd/server