from typing import List

from fastapi import APIRouter, Depends, HTTPException, status

from app.db.session import get_cursor
from app.schemas.cabinet_schema import CabinetCreate, CabinetUpdate, CabinetOut

router = APIRouter(prefix="/cabinets", tags=["cabinets"])


# ---------- Helpers ----------

def _get_cabinet_or_404(cursor, cabinet_id: int) -> dict:
    cursor.execute(
        """
        SELECT id, naziv, transparentnost, slika
        FROM orman
        WHERE id = %s
        """,
        (cabinet_id,),
    )
    row = cursor.fetchone()
    if not row:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail=f"Cabinet with id={cabinet_id} not found",
        )
    return row


# ---------- Routes ----------

@router.get("/", response_model=List[CabinetOut])
def list_cabinets(cursor=Depends(get_cursor)):
    """
    List all cabinets ordered by name.
    """
    cursor.execute(
        """
        SELECT id, naziv, transparentnost, slika
        FROM orman
        ORDER BY naziv
        """
    )
    return cursor.fetchall()


@router.get("/{cabinet_id}", response_model=CabinetOut)
def get_cabinet(cabinet_id: int, cursor=Depends(get_cursor)):
    """
    Get a single cabinet by ID.
    """
    return _get_cabinet_or_404(cursor, cabinet_id)


@router.post(
    "/",
    response_model=CabinetOut,
    status_code=status.HTTP_201_CREATED,
)
def create_cabinet(payload: CabinetCreate, cursor=Depends(get_cursor)):
    """
    Create a new cabinet.
    """
    cursor.execute(
        """
        INSERT INTO orman (naziv, transparentnost, slika)
        VALUES (%s, %s, %s)
        """,
        (payload.naziv, payload.transparentnost, payload.slika),
    )
    new_id = cursor.lastrowid

    cursor.execute(
        """
        SELECT id, naziv, transparentnost, slika
        FROM orman
        WHERE id = %s
        """,
        (new_id,),
    )
    return cursor.fetchone()


@router.put("/{cabinet_id}", response_model=CabinetOut)
def update_cabinet(
    cabinet_id: int,
    payload: CabinetCreate,
    cursor=Depends(get_cursor),
):
    """
    Full update of a cabinet.
    """
    _get_cabinet_or_404(cursor, cabinet_id)

    cursor.execute(
        """
        UPDATE orman
        SET naziv = %s,
            transparentnost = %s,
            slika = %s
        WHERE id = %s
        """,
        (payload.naziv, payload.transparentnost, payload.slika, cabinet_id),
    )

    cursor.execute(
        """
        SELECT id, naziv, transparentnost, slika
        FROM orman
        WHERE id = %s
        """,
        (cabinet_id,),
    )
    return cursor.fetchone()


@router.patch("/{cabinet_id}", response_model=CabinetOut)
def patch_cabinet(
    cabinet_id: int,
    payload: CabinetUpdate,
    cursor=Depends(get_cursor),
):
    """
    Partial update of a cabinet.
    """
    existing = _get_cabinet_or_404(cursor, cabinet_id)

    new_naziv = payload.naziv if payload.naziv is not None else existing["naziv"]
    new_transparentnost = (
        payload.transparentnost
        if payload.transparentnost is not None
        else existing["transparentnost"]
    )
    new_slika = payload.slika if payload.slika is not None else existing["slika"]

    cursor.execute(
        """
        UPDATE orman
        SET naziv = %s,
            transparentnost = %s,
            slika = %s
        WHERE id = %s
        """,
        (new_naziv, new_transparentnost, new_slika, cabinet_id),
    )

    cursor.execute(
        """
        SELECT id, naziv, transparentnost, slika
        FROM orman
        WHERE id = %s
        """,
        (cabinet_id,),
    )
    return cursor.fetchone()


@router.delete("/{cabinet_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_cabinet(cabinet_id: int, cursor=Depends(get_cursor)):
    """
    Delete a cabinet by ID.
    """
    _get_cabinet_or_404(cursor, cabinet_id)
    cursor.execute(
        "DELETE FROM orman WHERE id = %s",
        (cabinet_id,),
    )
