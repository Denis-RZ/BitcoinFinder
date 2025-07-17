#!/bin/bash

# Скрипт для тестирования Cron API BitcoinFinder
# Использование: ./test_cron_api.sh [domain]

DOMAIN=${1:-"localhost:5002"}
PROTOCOL="http"

echo "🔧 Тестирование Cron API BitcoinFinder"
echo "🌐 Домен: $PROTOCOL://$DOMAIN"
echo "⏰ Время: $(date)"
echo ""

# Функция для выполнения запроса
test_api() {
    local endpoint=$1
    local method=${2:-"GET"}
    local data=${3:-""}
    
    echo "📡 Тестируем: $method $endpoint"
    
    if [ -n "$data" ]; then
        response=$(curl -s -X $method "$PROTOCOL://$DOMAIN$endpoint" \
            -H "Content-Type: application/json" \
            -d "$data" \
            -w "\nHTTP_CODE:%{http_code}\nTIME:%{time_total}s")
    else
        response=$(curl -s -X $method "$PROTOCOL://$DOMAIN$endpoint" \
            -w "\nHTTP_CODE:%{http_code}\nTIME:%{time_total}s")
    fi
    
    http_code=$(echo "$response" | grep "HTTP_CODE:" | cut -d: -f2)
    time_total=$(echo "$response" | grep "TIME:" | cut -d: -f2)
    json_response=$(echo "$response" | grep -v "HTTP_CODE:" | grep -v "TIME:")
    
    echo "📊 HTTP код: $http_code"
    echo "⏱️  Время ответа: ${time_total}s"
    
    if [ "$http_code" = "200" ]; then
        echo "✅ Успешно"
        echo "📄 Ответ: $json_response" | head -c 200
        echo "..."
    else
        echo "❌ Ошибка"
        echo "📄 Ответ: $json_response"
    fi
    echo ""
}

# Тестируем основные endpoints
echo "=== 🔍 Основные тесты ==="

test_api "/api/cron/ping"
test_api "/api/keep-alive/health"
test_api "/api/cron/tasks-status"

echo "=== 🎯 Тест создания задачи ==="

# Создаем тестовую задачу
task_data='{
    "name": "Cron Test Task",
    "targetAddress": "1MCirzugBCrn5H6jHix6PJSLX7EqUEniBQ",
    "wordCount": 12,
    "startIndex": 0,
    "endIndex": 10000,
    "blockSize": 1000,
    "threads": 1,
    "autoStart": false
}'

test_api "/api/cron/create-task" "POST" "$task_data"

echo "=== 🔄 Тест сброса пула ==="
test_api "/api/cron/reset-pool" "POST"

echo "=== 📊 Финальная проверка ==="
test_api "/api/cron/tasks-status"

echo "✅ Тестирование завершено!"
echo "📝 Для настройки cron используйте:"
echo "*/15 * * * * curl -s \"$PROTOCOL://$DOMAIN/api/cron/ping\" > /dev/null 2>&1" 