@echo off
chcp 65001 > nul
cd /d %~dp0

echo index.lock を削除中...
if exist .git\index.lock (
    takeown /f .git\index.lock > nul 2>&1
    del /f /q .git\index.lock > nul 2>&1
)

echo ブランチ切り替え中...
git checkout -b feature/bake-mesh-asset-save 2>nul || git checkout feature/bake-mesh-asset-save

echo コミットメッセージ書き込み中...
echo feat: save baked mesh as FBX/.asset for Prefab support > .git\COMMIT_MSG_TMP

echo add/commit 中...
git add -A
git commit -F .git\COMMIT_MSG_TMP
del /q .git\COMMIT_MSG_TMP 2>nul

echo push 中...
git push -u origin feature/bake-mesh-asset-save

echo.
echo === 完了。このウィンドウを閉じてください ===
pause
