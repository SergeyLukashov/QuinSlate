// CRLF-aware text helpers. The document uses "\n" internally; the buffer file
// (and the character cap) count each line break as CRLF = 2 chars. Effective
// length is therefore the character count plus one extra per line break.

export function crlfLengthOfDoc(doc) {
  return doc.length + (doc.lines - 1);
}

export function crlfLengthOfString(text) {
  let breaks = 0;
  for (let i = 0; i < text.length; i++) {
    if (text.charCodeAt(i) === 10) {
      breaks++;
    }
  }
  return text.length + breaks;
}

// Returns the longest prefix of text whose CRLF length does not exceed budget.
export function truncateToCrlfBudget(text, budget) {
  if (budget <= 0) {
    return "";
  }
  let used = 0;
  for (let i = 0; i < text.length; i++) {
    const cost = text.charCodeAt(i) === 10 ? 2 : 1;
    if (used + cost > budget) {
      return text.slice(0, i);
    }
    used += cost;
  }
  return text;
}

// Host text arrives with CRLF (disk convention); the document uses "\n".
export function normaliseIncoming(text) {
  if (text == null) {
    return "";
  }
  return text.replace(/\r\n?/g, "\n");
}
