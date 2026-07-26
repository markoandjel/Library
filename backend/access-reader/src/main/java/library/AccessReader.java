package library;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.healthmarketscience.jackcess.*;
import com.healthmarketscience.jackcess.complex.Attachment;
import com.healthmarketscience.jackcess.complex.ComplexValueForeignKey;
import java.io.File;
import java.time.temporal.TemporalAccessor;
import java.util.*;

public final class AccessReader {
    private static final ObjectMapper JSON = new ObjectMapper();

    public static void main(String[] args) {
        try {
            if (args.length < 2) throw new IllegalArgumentException("Usage: access-reader <preview|book|cover> <file> [bookId]");
            try (Database db = DatabaseBuilder.open(new File(args[1]))) {
                switch (args[0]) {
                    case "preview" -> preview(db);
                    case "book" -> book(db, requiredId(args));
                    case "cover" -> cover(db, requiredId(args));
                    default -> throw new IllegalArgumentException("Unknown command: " + args[0]);
                }
            }
        } catch (Exception error) {
            System.err.println(error.getMessage());
            error.printStackTrace(System.err);
            System.exit(1);
        }
    }

    private static int requiredId(String[] args) {
        if (args.length < 3) throw new IllegalArgumentException("bookId is required");
        return Integer.parseInt(args[2]);
    }

    private static void preview(Database db) throws Exception {
        List<Map<String, Object>> result = new ArrayList<>();
        for (Row row : table(db)) {
            Map<String, Object> item = new LinkedHashMap<>();
            item.put("id", integer(row.get("ID Knjiga")));
            item.put("title", text(row.get("Naslov")));
            item.put("authors", text(row.get("Autori niz")));
            Attachment attachment = firstAttachment(row.get("SlikaAttach"));
            item.put("imageFileName", attachment == null ? "" : attachment.getFileName());
            result.add(item);
        }
        result.sort(Comparator.comparingInt(item -> (Integer)item.get("id")));
        JSON.writeValue(System.out, result);
    }

    private static void book(Database db, int id) throws Exception {
        Row row = find(db, id);
        if (row == null) {
            System.out.print("null");
            return;
        }
        Map<String, Object> result = new LinkedHashMap<>();
        for (Column column : table(db).getColumns()) {
            Object value = row.get(column.getName());
            if (!(value instanceof ComplexValueForeignKey)) result.put(column.getName(), jsonValue(value));
        }
        result.put("_cover", attachmentMap(firstAttachment(row.get("SlikaAttach"))));
        JSON.writeValue(System.out, result);
    }

    private static void cover(Database db, int id) throws Exception {
        Row row = find(db, id);
        JSON.writeValue(System.out, row == null ? null : attachmentMap(firstAttachment(row.get("SlikaAttach"))));
    }

    private static Table table(Database db) throws Exception {
        Table table = db.getTable("Knjiga");
        if (table == null) throw new IllegalArgumentException("Tabela 'Knjiga' nije pronadjena u Access fajlu.");
        return table;
    }

    private static Row find(Database db, int id) throws Exception {
        for (Row row : table(db)) {
            if (integer(row.get("ID Knjiga")) == id) return row;
        }
        return null;
    }

    private static Attachment firstAttachment(Object value) throws Exception {
        if (!(value instanceof ComplexValueForeignKey key)) return null;
        List<Attachment> attachments = key.getAttachments();
        return attachments.isEmpty() ? null : attachments.get(0);
    }

    private static Map<String, Object> attachmentMap(Attachment attachment) throws Exception {
        if (attachment == null) return null;
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("fileName", attachment.getFileName());
        result.put("data", Base64.getEncoder().encodeToString(attachment.getFileData()));
        return result;
    }

    private static Object jsonValue(Object value) {
        if (value == null) return null;
        if (value instanceof byte[] bytes) return Base64.getEncoder().encodeToString(bytes);
        if (value instanceof Date date) return date.toInstant().toString();
        if (value instanceof TemporalAccessor) return value.toString();
        if (value instanceof Number || value instanceof Boolean || value instanceof String) return value;
        return value.toString();
    }

    private static int integer(Object value) {
        return value instanceof Number number ? number.intValue() : 0;
    }

    private static String text(Object value) {
        return value == null ? "" : value.toString();
    }
}
