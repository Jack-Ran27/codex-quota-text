@echo off
setlocal
set "QUOTA_TEXT_NODE=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
if defined CODEX_MCP_NODE_PATH if exist "%CODEX_MCP_NODE_PATH%" set "QUOTA_TEXT_NODE=%CODEX_MCP_NODE_PATH%"
if not exist "%QUOTA_TEXT_NODE%" exit /b 2
"%QUOTA_TEXT_NODE%" "%~dp0server.mjs"
