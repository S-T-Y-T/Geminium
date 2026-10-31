# Обновление дизайн-документа Geminium:
# схемы (diagrams.js) -> DOCX (build.js) -> Word обновляет оглавление -> Docs/Geminium_GDD.docx и .pdf
# Запуск из корня проекта: powershell -ExecutionPolicy Bypass -File Docs/src/update-docs.ps1
$ErrorActionPreference = 'Stop'

$src = $PSScriptRoot
$docsDir = Split-Path $src -Parent
$raw = Join-Path $src 'out\Geminium_GDD.raw.docx'

Push-Location $src
try {
  if (-not (Test-Path (Join-Path $src 'node_modules'))) { npm install --no-audit --no-fund }
  node diagrams.js
  if ($LASTEXITCODE -ne 0) { throw 'diagrams.js завершился с ошибкой' }
  node build.js
  if ($LASTEXITCODE -ne 0) { throw 'build.js завершился с ошибкой' }
} finally { Pop-Location }

# Word 2007 не умеет перезаписывать файл через SaveAs, поэтому сохраняем во временную папку и копируем
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) 'geminium-docs'
New-Item -ItemType Directory -Force $tmp | Out-Null
$tmpDocx = Join-Path $tmp 'Geminium_GDD.docx'
$tmpPdf = Join-Path $tmp 'Geminium_GDD.pdf'
Remove-Item $tmpDocx, $tmpPdf -ErrorAction SilentlyContinue

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
  $doc = $word.Documents.Open($raw)
  $doc.Repaginate()
  for ($i = 1; $i -le $doc.TablesOfContents.Count; $i++) { $doc.TablesOfContents.Item($i).Update() }
  $doc.Repaginate()
  for ($i = 1; $i -le $doc.TablesOfContents.Count; $i++) { $doc.TablesOfContents.Item($i).UpdatePageNumbers() }
  $doc.SaveAs($tmpDocx, 12)          # 12 = wdFormatXMLDocument (.docx)
  $doc.ExportAsFixedFormat($tmpPdf, 17)  # 17 = wdExportFormatPDF
  $pages = $doc.ComputeStatistics(2)
  $doc.Close(0)
} finally {
  try { $word.Quit() } catch {}
}

Copy-Item $tmpDocx, $tmpPdf -Destination $docsDir -Force
Write-Output "Готово: $pages стр. -> $docsDir\Geminium_GDD.docx и .pdf"
