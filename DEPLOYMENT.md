# Инструкции по развертыванию BitcoinFinder Web Server

## Подготовка к развертыванию

### 1. Требования к серверу

- **Операционная система**: Windows Server 2019/2022 или Linux (Ubuntu 20.04+)
- **.NET Runtime**: .NET 8.0 или выше
- **База данных**: SQL Server 2019+ или SQL Server Express
- **Память**: Минимум 2GB RAM
- **Диск**: Минимум 10GB свободного места

### 2. Установка .NET Runtime

#### Windows
```bash
# Скачайте и установите .NET 8.0 Runtime с официального сайта Microsoft
# https://dotnet.microsoft.com/download/dotnet/8.0
```

#### Linux (Ubuntu)
```bash
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y apt-transport-https
sudo apt-get install -y dotnet-runtime-8.0
```

### 3. Установка SQL Server

#### Windows
```bash
# Установите SQL Server Express или Developer Edition
# https://www.microsoft.com/en-us/sql-server/sql-server-downloads
```

#### Linux (Docker)
```bash
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong@Passw0rd" \
   -p 1433:1433 --name sql1 --hostname sql1 \
   -d mcr.microsoft.com/mssql/server:2019-latest
```

## Развертывание

### 1. Публикация приложения

```bash
# В папке проекта
dotnet publish -c Release -o ./publish
```

### 2. Настройка конфигурации

Создайте файл `appsettings.Production.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=your-server;Database=BitcoinFinderWebServer;User Id=your-user;Password=your-password;TrustServerCertificate=true"
  },
  "PoolSettings": {
    "ResetIntervalMinutes": 30,
    "CleanupIntervalMinutes": 5
  },
  "ServerSettings": {
    "MaxTasksPerAgent": 1,
    "TaskTimeoutMinutes": 60
  },
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      },
      "Https": {
        "Url": "https://0.0.0.0:5001"
      }
    }
  }
}
```

### 3. Создание базы данных

```sql
-- Подключитесь к SQL Server и выполните:
CREATE DATABASE BitcoinFinderWebServer;
GO
USE BitcoinFinderWebServer;
GO
```

### 4. Настройка службы Windows

Создайте файл `BitcoinFinderWebServer.service`:

```ini
[Unit]
Description=BitcoinFinder Web Server
After=network.target

[Service]
Type=notify
ExecStart=/usr/bin/dotnet /var/www/bitcoinfinder/BitcoinFinderWebServer.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=bitcoinfinder
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```

### 5. Настройка Nginx (Linux)

Создайте файл `/etc/nginx/sites-available/bitcoinfinder`:

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Активируйте сайт:
```bash
sudo ln -s /etc/nginx/sites-available/bitcoinfinder /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

### 6. Настройка SSL (Let's Encrypt)

```bash
sudo apt-get install certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com
```

## Мониторинг и обслуживание

### 1. Логи

Логи приложения находятся в:
- Windows: `%ProgramData%\BitcoinFinderWebServer\logs\`
- Linux: `/var/log/bitcoinfinder/`

### 2. Мониторинг

Настройте мониторинг для:
- Доступности веб-интерфейса
- Использования памяти и CPU
- Состояния базы данных
- Количества подключенных агентов

### 3. Резервное копирование

Настройте регулярное резервное копирование:
- База данных SQL Server
- Конфигурационные файлы
- Логи приложения

### 4. Обновления

Для обновления приложения:
1. Остановите службу
2. Создайте резервную копию
3. Замените файлы приложения
4. Запустите службу

## Безопасность

### 1. Firewall

Настройте firewall для доступа только к необходимым портам:
- 80 (HTTP)
- 443 (HTTPS)
- 1433 (SQL Server, если внешний доступ)

### 2. Аутентификация

Для продакшена рекомендуется добавить аутентификацию в веб-интерфейс.

### 3. SSL/TLS

Обязательно используйте SSL/TLS для защиты данных.

## Troubleshooting

### Частые проблемы

1. **Ошибка подключения к базе данных**
   - Проверьте строку подключения
   - Убедитесь, что SQL Server запущен
   - Проверьте права доступа пользователя

2. **Порт занят**
   - Измените порт в конфигурации
   - Проверьте, что другие приложения не используют порт

3. **Ошибки CORS**
   - Настройте CORS политики в `Program.cs`
   - Проверьте настройки прокси-сервера

### Логи

Для диагностики проблем проверьте:
- Логи приложения
- Логи веб-сервера (Nginx/Apache)
- Логи SQL Server
- Системные логи

## Производительность

### Рекомендации

1. **База данных**
   - Используйте SSD для базы данных
   - Настройте индексы для таблиц
   - Регулярно выполняйте обслуживание

2. **Приложение**
   - Мониторьте использование памяти
   - Настройте пул соединений
   - Используйте кэширование при необходимости

3. **Сеть**
   - Используйте CDN для статических файлов
   - Настройте сжатие
   - Оптимизируйте размер ответов API 