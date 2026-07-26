# Event-driven .NET reference — local environment.

.PHONY: up down logs demo build ps

## up: build and start RabbitMQ, PostgreSQL, and the three services
up:
	docker compose up -d --build
	@echo "waiting for the ordering API..."
	@for i in $$(seq 1 60); do \
		curl -sf http://localhost:8080/health >/dev/null 2>&1 && { echo "up — API on :8080, RabbitMQ UI on :15672 (guest/guest)"; exit 0; }; \
		sleep 1; \
	done; echo "ordering API did not come up in time"; docker compose logs ordering; exit 1

## down: stop everything and drop volumes
down:
	docker compose down -v

## logs: follow all service logs (add S=payments to follow one)
logs:
	docker compose logs -f $(S)

## demo: place orders and watch them flow — happy path, decline, poison/DLQ, replay/idempotency
demo:
	./scripts/demo.sh

## ps: show container status
ps:
	docker compose ps

## build: compile the solution
build:
	dotnet build EventDriven.sln -c Release
