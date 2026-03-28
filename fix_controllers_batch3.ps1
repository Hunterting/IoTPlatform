# 批量修复Controller响应格式错误的PowerShell脚本 - 第三批

$controllersPath = "g:\IoTPlatform\Controllers"

# 需要修复的文件列表 - 第三批
$files = @(
    "DictionariesController.cs",
    "ETLTasksController.cs",
    "LogsController.cs",
    "MonitoringController.cs",
    "ProtocolConfigsController.cs",
    "RolesController.cs"
)

foreach ($file in $files) {
    $filePath = Join-Path $controllersPath $file
    Write-Host "Processing $file..."
    
    # 读取文件内容
    $content = Get-Content $filePath -Raw -Encoding UTF8
    
    # 正则表达式替换所有 `return Ok(ApiResponse<T>.XXX("消息"))` 格式
    $pattern = 'return Ok\(ApiResponse(?:<[^>]+>)?\.(\w+)\(([^)]+)\)\);'
    $replacement = 'var response = ApiResponse$1($2);' + "`r`n            return Ok(response);"
    
    $newContent = [regex]::Replace($content, $pattern, $replacement)
    
    # 如果内容有变化，写入文件
    if ($newContent -ne $content) {
        Set-Content $filePath $newContent -Encoding UTF8 -NoNewline
        Write-Host "Fixed $file"
    } else {
        Write-Host "No changes in $file"
    }
}

Write-Host "Done!"
