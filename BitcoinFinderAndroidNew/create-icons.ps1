# Создание простых PNG иконок для Android
Add-Type -AssemblyName System.Drawing

# Функция для создания простой иконки
function Create-Icon {
    param(
        [string]$Path,
        [int]$Size
    )
    
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    
    # Заливка фона
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Blue)
    $graphics.FillRectangle($brush, 0, 0, $Size, $Size)
    
    # Рисуем простой символ
    $font = New-Object System.Drawing.Font("Arial", $Size/3, [System.Drawing.FontStyle]::Bold)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $text = "B"
    $textSize = $graphics.MeasureString($text, $font)
    $x = ($Size - $textSize.Width) / 2
    $y = ($Size - $textSize.Height) / 2
    $graphics.DrawString($text, $font, $textBrush, $x, $y)
    
    # Сохраняем
    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
}

# Создаем иконки для разных разрешений
$resolutions = @{
    "mipmap-mdpi" = 48
    "mipmap-hdpi" = 72
    "mipmap-xhdpi" = 96
    "mipmap-xxhdpi" = 144
}

foreach ($resolution in $resolutions.GetEnumerator()) {
    $folder = "Platforms\Android\Resources\$($resolution.Key)"
    $appicon = "$folder\appicon.png"
    $appicon_round = "$folder\appicon_round.png"
    
    Create-Icon -Path $appicon -Size $resolution.Value
    Create-Icon -Path $appicon_round -Size $resolution.Value
    
    Write-Host "Created icons for $($resolution.Key)"
}

Write-Host "All icons created successfully!" 