# 修复PowerShell脚本导致的错误
# 将 ApiResponseError, ApiResponseNotFound 等错误名称改回正确的形式

$controllersPath = "g:\IoTPlatform\Controllers"

# 获取所有.cs文件
$files = Get-ChildItem -Path $controllersPath -Filter "*.cs"

foreach ($file in $files) {
    $filePath = $file.FullName
    Write-Host "Processing $($file.Name)..."
    
    # 读取文件内容
    $content = Get-Content $filePath -Raw -Encoding UTF8
    
    $originalContent = $content
    
    # 修复各种错误的模式
    $content = $content -replace 'var response = ApiResponseError\(', 'var response = ApiResponse.Error('
    $content = $content -replace 'var response = ApiResponseNotFound\(', 'var response = ApiResponse.NotFound('
    $content = $content -replace 'var response = ApiResponseBadRequest\(', 'var response = ApiResponse.BadRequest('
    $content = $content -replace 'var response = ApiResponseForbidden\(', 'var response = ApiResponse.Forbidden('
    $content = $content -replace 'var response = ApiResponseUnauthorized\(', 'var response = ApiResponse.Unauthorized('
    $content = $content -replace 'var response = ApiResponseSuccess\(', 'var response = ApiResponse.Success('
    
    # 如果内容有变化，写入文件
    if ($content -ne $originalContent) {
        Set-Content $filePath $content -Encoding UTF8 -NoNewline
        Write-Host "Fixed $($file.Name)"
    } else {
        Write-Host "No changes in $($file.Name)"
    }
}

Write-Host "Done!"
