from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, status

from app.db.session import get_cursor
from app.schemas.book_schema import BookCreate, BookUpdate, BookOut

router = APIRouter(prefix="/books", tags=["books"])


# =========================================================
# Helpers: base book
# =========================================================

BOOK_SELECT_COLUMNS = """
    id,
    naslov,
    primedba_naslov,
    izdavac_id,
    godina,
    broj_strana,
    jezik_id,
    originalni_jezik_id,
    pismo_id,
    prevod,
    isbn,
    primedba_knjiga,
    domaci_autor,
    strani_autor,
    tvrdi_povez,
    kolor,
    fotokopija,
    sirina,
    visina,
    debljina,
    broj_primeraka,
    vreme,
    slika_nepotrebna,
    slika_velika,
    slika_unutrasnja,
    knjiga_id,
    broj_tomova,
    polica_id
"""


def _get_book_or_404(cursor, book_id: int) -> dict:
    cursor.execute(
        f"""
        SELECT {BOOK_SELECT_COLUMNS}
        FROM knjiga
        WHERE id = %s
        """,
        (book_id,),
    )
    row = cursor.fetchone()
    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Book with id={book_id} not found",
        )
    return row


# =========================================================
# Helpers: many-to-many sync & load
# =========================================================

def _sync_relation(
    cursor,
    table: str,
    id_column: str,
    book_id: int,
    ids: Optional[List[int]],
):
    """
    Sync a many-to-many relation table for one book.

    Behavior:
      - ids is None  -> do nothing (leave current state)
      - ids is []    -> remove all rows for that book
      - ids has vals -> replace with exactly those ids
    """
    if ids is None:
        return

    # clear existing rows
    cursor.execute(
        f"DELETE FROM {table} WHERE knjiga_id = %s",
        (book_id,),
    )

    if not ids:
        return

    values = [(book_id, _id) for _id in ids]
    placeholders = ", ".join(["(%s, %s)"] * len(values))
    flat_params = [item for pair in values for item in pair]

    cursor.execute(
        f"""
        INSERT INTO {table} (knjiga_id, {id_column})
        VALUES {placeholders}
        """,
        flat_params,
    )


def _load_all_relations(cursor, books: List[dict]) -> None:
    """
    For each book dict in `books`, attaches:
      - kategorija_ids
      - autor_ids
      - jezik_ids
      - jezik_orig_ids
      - pismo_ids
    using one query per relation table.
    """
    if not books:
        return

    book_ids = [b["id"] for b in books]
    ids_tuple = tuple(book_ids)

    def fetch_map(table: str, id_column: str) -> dict[int, List[int]]:
        if not ids_tuple:
            return {}
        placeholders = ", ".join(["%s"] * len(ids_tuple))
        cursor.execute(
            f"""
            SELECT knjiga_id, {id_column}
            FROM {table}
            WHERE knjiga_id IN ({placeholders})
            """,
            ids_tuple,
        )
        mapping: dict[int, List[int]] = {}
        rows = cursor.fetchall()
        if not rows:
            return mapping
        for row in rows:
            k_id = row["knjiga_id"]
            v_id = row[id_column]
            mapping.setdefault(k_id, []).append(v_id)
        return mapping

    kategorija_map = fetch_map("kategorijaknjiga", "kategorija_id")
    autor_map = fetch_map("autorknjiga", "autor_id")
    jezik_map = fetch_map("jezikknjiga", "jezik_id")
    jezik_orig_map = fetch_map("jezikoriginalknjiga", "jezik_original_id")
    pismo_map = fetch_map("pismoknjiga", "pismo_id")

    for b in books:
        bid = b["id"]
        b["kategorija_ids"] = kategorija_map.get(bid, [])
        b["autor_ids"] = autor_map.get(bid, [])
        b["jezik_ids"] = jezik_map.get(bid, [])
        b["jezik_orig_ids"] = jezik_orig_map.get(bid, [])
        b["pismo_ids"] = pismo_map.get(bid, [])


def _load_relations_for_one(cursor, book: dict) -> None:
    _load_all_relations(cursor, [book])


# =========================================================
# Routes
# =========================================================

@router.get("/", response_model=List[BookOut])
def list_books(cursor=Depends(get_cursor)):
    """
    List all books with related ID lists from junction tables.
    """
    cursor.execute(
        f"""
        SELECT {BOOK_SELECT_COLUMNS}
        FROM knjiga
        ORDER BY naslov
        """
    )
    rows = cursor.fetchall()
    _load_all_relations(cursor, rows)
    return rows


@router.get("/{book_id}", response_model=BookOut)
def get_book(book_id: int, cursor=Depends(get_cursor)):
    """
    Get a single book with related ID lists.
    """
    book = _get_book_or_404(cursor, book_id)
    _load_relations_for_one(cursor, book)
    return book


@router.post("/", response_model=BookOut, status_code=status.HTTP_201_CREATED)
def create_book(payload: BookCreate, cursor=Depends(get_cursor)):
    """
    Create a new book + many-to-many relations.
    """
    base_data = payload.model_dump(
        exclude={
            "kategorija_ids",
            "autor_ids",
            "jezik_ids",
            "jezik_orig_ids",
            "pismo_ids",
        }
    )

    cursor.execute(
        f"""
        INSERT INTO knjiga (
            naslov,
            primedba_naslov,
            izdavac_id,
            godina,
            broj_strana,
            jezik_id,
            originalni_jezik_id,
            pismo_id,
            prevod,
            isbn,
            primedba_knjiga,
            domaci_autor,
            strani_autor,
            tvrdi_povez,
            kolor,
            fotokopija,
            sirina,
            visina,
            debljina,
            broj_primeraka,
            vreme,
            slika_nepotrebna,
            slika_velika,
            slika_unutrasnja,
            knjiga_id,
            broj_tomova,
            polica_id
        )
        VALUES (
            %(naslov)s,
            %(primedba_naslov)s,
            %(izdavac_id)s,
            %(godina)s,
            %(broj_strana)s,
            %(jezik_id)s,
            %(originalni_jezik_id)s,
            %(pismo_id)s,
            %(prevod)s,
            %(isbn)s,
            %(primedba_knjiga)s,
            %(domaci_autor)s,
            %(strani_autor)s,
            %(tvrdi_povez)s,
            %(kolor)s,
            %(fotokopija)s,
            %(sirina)s,
            %(visina)s,
            %(debljina)s,
            %(broj_primeraka)s,
            %(vreme)s,
            %(slika_nepotrebna)s,
            %(slika_velika)s,
            %(slika_unutrasnja)s,
            %(knjiga_id)s,
            %(broj_tomova)s,
            %(polica_id)s
        )
        """,
        base_data,
    )

    new_id = cursor.lastrowid

    # Many-to-many relations
    _sync_relation(cursor, "kategorijaknjiga", "kategorija_id", new_id, payload.kategorija_ids)
    _sync_relation(cursor, "autorknjiga", "autor_id", new_id, payload.autor_ids)
    _sync_relation(cursor, "jezikknjiga", "jezik_id", new_id, payload.jezik_ids)
    _sync_relation(cursor, "jezikoriginalknjiga", "jezik_original_id", new_id, payload.jezik_orig_ids)
    _sync_relation(cursor, "pismoknjiga", "pismo_id", new_id, payload.pismo_ids)

    book = _get_book_or_404(cursor, new_id)
    _load_relations_for_one(cursor, book)
    return book


@router.put("/{book_id}", response_model=BookOut)
def update_book(
    book_id: int,
    payload: BookCreate,
    cursor=Depends(get_cursor),
):
    """
    Full update of a book.
    Also replaces many-to-many relations if lists are provided.
    """
    _get_book_or_404(cursor, book_id)

    base_data = payload.model_dump(
        exclude={
            "kategorija_ids",
            "autor_ids",
            "jezik_ids",
            "jezik_orig_ids",
            "pismo_ids",
        }
    )
    base_data["id"] = book_id

    cursor.execute(
        f"""
        UPDATE knjiga
        SET
            naslov = %(naslov)s,
            primedba_naslov = %(primedba_naslov)s,
            izdavac_id = %(izdavac_id)s,
            godina = %(godina)s,
            broj_strana = %(broj_strana)s,
            jezik_id = %(jezik_id)s,
            originalni_jezik_id = %(originalni_jezik_id)s,
            pismo_id = %(pismo_id)s,
            prevod = %(prevod)s,
            isbn = %(isbn)s,
            primedba_knjiga = %(primedba_knjiga)s,
            domaci_autor = %(domaci_autor)s,
            strani_autor = %(strani_autor)s,
            tvrdi_povez = %(tvrdi_povez)s,
            kolor = %(kolor)s,
            fotokopija = %(fotokopija)s,
            sirina = %(sirina)s,
            visina = %(visina)s,
            debljina = %(debljina)s,
            broj_primeraka = %(broj_primeraka)s,
            vreme = %(vreme)s,
            slika_nepotrebna = %(slika_nepotrebna)s,
            slika_velika = %(slika_velika)s,
            slika_unutrasnja = %(slika_unutrasnja)s,
            knjiga_id = %(knjiga_id)s,
            broj_tomova = %(broj_tomova)s,
            polica_id = %(polica_id)s
        WHERE id = %(id)s
        """,
        base_data,
    )

    # Replace relations (None leaves as is, [] clears)
    _sync_relation(cursor, "kategorijaknjiga", "kategorija_id", book_id, payload.kategorija_ids)
    _sync_relation(cursor, "autorknjiga", "autor_id", book_id, payload.autor_ids)
    _sync_relation(cursor, "jezikknjiga", "jezik_id", book_id, payload.jezik_ids)
    _sync_relation(cursor, "jezikoriginalknjiga", "jezik_original_id", book_id, payload.jezik_orig_ids)
    _sync_relation(cursor, "pismoknjiga", "pismo_id", book_id, payload.pismo_ids)

    book = _get_book_or_404(cursor, book_id)
    _load_relations_for_one(cursor, book)
    return book


@router.patch("/{book_id}", response_model=BookOut)
def patch_book(
    book_id: int,
    payload: BookUpdate,
    cursor=Depends(get_cursor),
):
    """
    Partial update of a book.
    Only fields present in payload are changed.
    Many-to-many lists:
      - not provided → unchanged
      - []           → clear
      - [ids]        → replace
    """
    existing = _get_book_or_404(cursor, book_id)
    update_data = payload.model_dump(exclude_unset=True)

    # Update scalar fields in-memory
    data = existing.copy()
    for key, value in update_data.items():
        if key not in {
            "kategorija_ids",
            "autor_ids",
            "jezik_ids",
            "jezik_orig_ids",
            "pismo_ids",
        }:
            data[key] = value

    data["id"] = book_id

    # Persist scalar changes
    cursor.execute(
        f"""
        UPDATE knjiga
        SET
            naslov = %(naslov)s,
            primedba_naslov = %(primedba_naslov)s,
            izdavac_id = %(izdavac_id)s,
            godina = %(godina)s,
            broj_strana = %(broj_strana)s,
            jezik_id = %(jezik_id)s,
            originalni_jezik_id = %(originalni_jezik_id)s,
            pismo_id = %(pismo_id)s,
            prevod = %(prevod)s,
            isbn = %(isbn)s,
            primedba_knjiga = %(primedba_knjiga)s,
            domaci_autor = %(domaci_autor)s,
            strani_autor = %(strani_autor)s,
            tvrdi_povez = %(tvrdi_povez)s,
            kolor = %(kolor)s,
            fotokopija = %(fotokopija)s,
            sirina = %(sirina)s,
            visina = %(visina)s,
            debljina = %(debljina)s,
            broj_primeraka = %(broj_primeraka)s,
            vreme = %(vreme)s,
            slika_nepotrebna = %(slika_nepotrebna)s,
            slika_velika = %(slika_velika)s,
            slika_unutrasnja = %(slika_unutrasnja)s,
            knjiga_id = %(knjiga_id)s,
            broj_tomova = %(broj_tomova)s,
            polica_id = %(polica_id)s
        WHERE id = %(id)s
        """,
        data,
    )

    # Relations: only touch if key present in payload
    _sync_relation(
        cursor,
        "kategorijaknjiga",
        "kategorija_id",
        book_id,
        update_data.get("kategorija_ids"),
    )
    _sync_relation(
        cursor,
        "autorknjiga",
        "autor_id",
        book_id,
        update_data.get("autor_ids"),
    )
    _sync_relation(
        cursor,
        "jezikknjiga",
        "jezik_id",
        book_id,
        update_data.get("jezik_ids"),
    )
    _sync_relation(
        cursor,
        "jezikoriginalknjiga",
        "jezik_original_id",
        book_id,
        update_data.get("jezik_orig_ids"),
    )
    _sync_relation(
        cursor,
        "pismoknjiga",
        "pismo_id",
        book_id,
        update_data.get("pismo_ids"),
    )

    book = _get_book_or_404(cursor, book_id)
    _load_relations_for_one(cursor, book)
    return book


@router.delete("/{book_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_book(book_id: int, cursor=Depends(get_cursor)):
    """
    Delete a book and its many-to-many relations.
    """
    _get_book_or_404(cursor, book_id)

    # clean up relations first
    cursor.execute("DELETE FROM kategorijaknjiga WHERE knjiga_id = %s", (book_id,))
    cursor.execute("DELETE FROM autorknjiga WHERE knjiga_id = %s", (book_id,))
    cursor.execute("DELETE FROM jezikknjiga WHERE knjiga_id = %s", (book_id,))
    cursor.execute("DELETE FROM jezikoriginalknjiga WHERE knjiga_id = %s", (book_id,))
    cursor.execute("DELETE FROM pismoknjiga WHERE knjiga_id = %s", (book_id,))

    # then delete the book
    cursor.execute("DELETE FROM knjiga WHERE id = %s", (book_id,))
