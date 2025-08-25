-- Initialize AutoConnect Database
-- This script runs when the PostgreSQL container starts for the first time

-- Create additional schemas if needed
-- CREATE SCHEMA IF NOT EXISTS autoconnect;

-- Set timezone
SET timezone = 'UTC';

-- Create extensions that might be useful
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Grant permissions to the application user
GRANT ALL PRIVILEGES ON DATABASE "AutoConnectDB" TO autoconnect_user;
GRANT ALL PRIVILEGES ON SCHEMA public TO autoconnect_user;

-- Create indexes for better performance (these will be created by EF migrations, but can be pre-created)
-- Note: The actual tables will be created by Entity Framework migrations

-- Log the initialization
INSERT INTO pg_stat_statements_info VALUES ('AutoConnect DB initialized at ' || NOW());

-- Set default search path
ALTER USER autoconnect_user SET search_path = public;