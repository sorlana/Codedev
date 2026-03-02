-- Удаление всех планов задач
DELETE FROM SubTaskEntity;
DELETE FROM PlannedTaskEntity;
DELETE FROM TaskPlans;

-- Проверка
SELECT COUNT(*) as RemainingPlans FROM TaskPlans;
