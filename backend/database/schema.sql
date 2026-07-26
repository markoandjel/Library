CREATE DATABASE IF NOT EXISTS `library`
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE `library`;

CREATE TABLE IF NOT EXISTS orman (
  id INT NOT NULL AUTO_INCREMENT,
  transparentnost TINYINT(1) NULL,
  naziv VARCHAR(255) NULL,
  slika VARCHAR(255) NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS polica (
  id INT NOT NULL AUTO_INCREMENT,
  x INT NULL,
  y INT NULL,
  orman_id INT NULL,
  PRIMARY KEY (id),
  INDEX ix_polica_orman_id (orman_id),
  CONSTRAINT fk_polica_orman
    FOREIGN KEY (orman_id) REFERENCES orman(id)
    ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS jezik (
  id INT NOT NULL AUTO_INCREMENT,
  naziv VARCHAR(100) NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS izdavac (
  id INT NOT NULL AUTO_INCREMENT,
  naziv VARCHAR(255) NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS pismo (
  id INT NOT NULL AUTO_INCREMENT,
  naziv VARCHAR(100) NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS kategorija (
  id INT NOT NULL AUTO_INCREMENT,
  naziv VARCHAR(255) NULL,
  opis VARCHAR(255) NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS autor (
  id INT NOT NULL AUTO_INCREMENT,
  ime VARCHAR(255) NULL,
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS knjiga (
  id INT NOT NULL AUTO_INCREMENT,
  naslov VARCHAR(255) NULL,
  primedba_naslov VARCHAR(255) NULL,
  izdavac_id INT NULL,
  godina INT NULL,
  broj_strana INT NULL,
  jezik_id INT NULL,
  originalni_jezik_id INT NULL,
  pismo_id INT NULL,
  prevod TINYINT(1) NULL,
  isbn VARCHAR(100) NULL,
  primedba_knjiga TEXT NULL,
  domaci_autor TINYINT(1) NULL,
  strani_autor TINYINT(1) NULL,
  tvrdi_povez TINYINT(1) NULL,
  kolor TINYINT(1) NULL,
  fotokopija TINYINT(1) NULL,
  sirina DECIMAL(5,2) NULL,
  visina DECIMAL(5,2) NULL,
  debljina DECIMAL(5,2) NULL,
  broj_primeraka INT NULL,
  vreme VARCHAR(50) NULL,
  slika_nepotrebna TINYINT(1) NULL,
  slika_velika TINYINT(1) NULL,
  slika_unutrasnja TINYINT(1) NULL,
  knjiga_id INT NULL,
  broj_tomova INT NULL,
  polica_id INT NULL,
  PRIMARY KEY (id),
  INDEX ix_knjiga_izdavac_id (izdavac_id),
  INDEX ix_knjiga_jezik_id (jezik_id),
  INDEX ix_knjiga_originalni_jezik_id (originalni_jezik_id),
  INDEX ix_knjiga_pismo_id (pismo_id),
  INDEX ix_knjiga_knjiga_id (knjiga_id),
  INDEX ix_knjiga_polica_id (polica_id),
  CONSTRAINT fk_knjiga_izdavac FOREIGN KEY (izdavac_id) REFERENCES izdavac(id) ON DELETE SET NULL,
  CONSTRAINT fk_knjiga_jezik FOREIGN KEY (jezik_id) REFERENCES jezik(id) ON DELETE SET NULL,
  CONSTRAINT fk_knjiga_originalni_jezik FOREIGN KEY (originalni_jezik_id) REFERENCES jezik(id) ON DELETE SET NULL,
  CONSTRAINT fk_knjiga_pismo FOREIGN KEY (pismo_id) REFERENCES pismo(id) ON DELETE SET NULL,
  CONSTRAINT fk_knjiga_parent FOREIGN KEY (knjiga_id) REFERENCES knjiga(id) ON DELETE SET NULL,
  CONSTRAINT fk_knjiga_polica FOREIGN KEY (polica_id) REFERENCES polica(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS slika (
  id INT NOT NULL AUTO_INCREMENT,
  naziv VARCHAR(255) NULL,
  knjiga_id INT NULL,
  PRIMARY KEY (id),
  INDEX ix_slika_knjiga_id (knjiga_id),
  CONSTRAINT fk_slika_knjiga FOREIGN KEY (knjiga_id) REFERENCES knjiga(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS kategorijaknjiga (
  id INT NOT NULL AUTO_INCREMENT,
  kategorija_id INT NULL,
  knjiga_id INT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_kategorijaknjiga (kategorija_id, knjiga_id),
  INDEX ix_kategorijaknjiga_knjiga_id (knjiga_id),
  CONSTRAINT fk_kategorijaknjiga_kategorija FOREIGN KEY (kategorija_id) REFERENCES kategorija(id) ON DELETE CASCADE,
  CONSTRAINT fk_kategorijaknjiga_knjiga FOREIGN KEY (knjiga_id) REFERENCES knjiga(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS autorknjiga (
  id INT NOT NULL AUTO_INCREMENT,
  knjiga_id INT NULL,
  autor_id INT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_autorknjiga (knjiga_id, autor_id),
  INDEX ix_autorknjiga_autor_id (autor_id),
  CONSTRAINT fk_autorknjiga_knjiga FOREIGN KEY (knjiga_id) REFERENCES knjiga(id) ON DELETE CASCADE,
  CONSTRAINT fk_autorknjiga_autor FOREIGN KEY (autor_id) REFERENCES autor(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS jezikknjiga (
  id INT NOT NULL AUTO_INCREMENT,
  jezik_id INT NULL,
  knjiga_id INT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_jezikknjiga (jezik_id, knjiga_id),
  INDEX ix_jezikknjiga_knjiga_id (knjiga_id),
  CONSTRAINT fk_jezikknjiga_jezik FOREIGN KEY (jezik_id) REFERENCES jezik(id) ON DELETE CASCADE,
  CONSTRAINT fk_jezikknjiga_knjiga FOREIGN KEY (knjiga_id) REFERENCES knjiga(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS jezikoriginalknjiga (
  id INT NOT NULL AUTO_INCREMENT,
  jezik_original_id INT NULL,
  knjiga_id INT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_jezikoriginalknjiga (jezik_original_id, knjiga_id),
  INDEX ix_jezikoriginalknjiga_knjiga_id (knjiga_id),
  CONSTRAINT fk_jezikoriginalknjiga_jezik FOREIGN KEY (jezik_original_id) REFERENCES jezik(id) ON DELETE CASCADE,
  CONSTRAINT fk_jezikoriginalknjiga_knjiga FOREIGN KEY (knjiga_id) REFERENCES knjiga(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS pismoknjiga (
  id INT NOT NULL AUTO_INCREMENT,
  pismo_id INT NULL,
  knjiga_id INT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY ux_pismoknjiga (pismo_id, knjiga_id),
  INDEX ix_pismoknjiga_knjiga_id (knjiga_id),
  CONSTRAINT fk_pismoknjiga_pismo FOREIGN KEY (pismo_id) REFERENCES pismo(id) ON DELETE CASCADE,
  CONSTRAINT fk_pismoknjiga_knjiga FOREIGN KEY (knjiga_id) REFERENCES knjiga(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
