UPDATE ExecutionSessions SET Status='stopped', CompletedAt=datetime('now') WHERE Status='running';
