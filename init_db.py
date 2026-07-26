import mysql.connector
from mysql.connector import errorcode
from pathlib import Path
from app.core.config import settings

DB_DIR = Path(__file__).resolve().parent.parent / "db"
SCHEMA_FILE = DB_DIR / "V1_initial.sql"

print("Using db dir:", DB_DIR)
print("Using schema file:", SCHEMA_FILE)


def run_sql_file(cursor, path: Path):
    sql = path.read_text(encoding="utf-8")

    # naive but effective splitter: split on ';' and execute non-empty statements
    statements = [s.strip() for s in sql.split(";") if s.strip()]

    for stmt in statements:
        try:
            cursor.execute(stmt)
        except mysql.connector.Error as err:
            print(f"❌ Error executing statement:\n{stmt}\n→ {err}")
            raise


def main():
    if not SCHEMA_FILE.exists():
        print(f"❌ SQL file not found: {SCHEMA_FILE}")
        return

    try:
        # 1) Ensure DB exists (connect WITHOUT database first)
        root_cnx = mysql.connector.connect(
            host=settings.DATABASE_URL,
            port=settings.DATABASE_PORT,
            user=settings.DATABASE_USER,
            password=settings.DATABASE_PASSWORD,
        )
        root_cursor = root_cnx.cursor()
        root_cursor.execute(
            f"CREATE DATABASE IF NOT EXISTS {settings.DATABASE_NAME} "
            "CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
        )
        root_cursor.close()
        root_cnx.close()
        print(f"✅ Database `{settings.DATABASE_NAME}` ensured.")

        # 2) Connect TO that DB
        cnx = mysql.connector.connect(
            host=settings.DATABASE_URL,
            port=settings.DATABASE_PORT,
            user=settings.DATABASE_USER,
            password=settings.DATABASE_PASSWORD,
            database=settings.DATABASE_NAME,
        )
        cursor = cnx.cursor()
        print(f"✅ Connected to `{settings.DATABASE_NAME}` as `{settings.DATABASE_USER}`.")

        # 3) Apply schema from file
        print(f"📦 Applying {SCHEMA_FILE.name} ...")
        run_sql_file(cursor, SCHEMA_FILE)

        cnx.commit()
        print("🎉 Schema applied successfully.")

    except mysql.connector.Error as err:
        if err.errno == errorcode.ER_ACCESS_DENIED_ERROR:
            print("❌ Access denied: check credentials in .env.")
        else:
            print(f"❌ MySQL error: {err}")
    except Exception as e:
        print(f"❌ Unexpected error: {e}")
    finally:
        try:
            cursor.close()
            cnx.close()
        except Exception:
            pass


if __name__ == "__main__":
    main()
