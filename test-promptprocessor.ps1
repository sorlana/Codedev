#!/usr/bin/env pwsh
# Test script for PromptProcessor integration tests
# Validates: Requirements 6.3, 4.3
# Task: 7.2 Ensure PromptProcessor uses factory-created LLM service

Write-Host "Running PromptProcessor Integration Tests..." -ForegroundColor Cyan
Write-Host ""

dotnet run --project CSharpRefactoringAssistant.csproj -- test-promptprocessor

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✓ All PromptProcessor tests passed!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "✗ Some tests failed!" -ForegroundColor Red
    exit 1
}
