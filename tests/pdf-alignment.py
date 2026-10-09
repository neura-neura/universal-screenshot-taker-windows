"""Compare exported PDF text rectangles against the actual OCR geometry."""
import json
import sys
from pathlib import Path
import fitz

pdf = fitz.open(sys.argv[1])
project = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8-sig'))
checked = 0
largest = 0
for page, capture in zip(pdf, project['pages']):
    expected = [w for line in capture['lines'] for w in line['words'] if w['text'].strip()]
    actual = page.get_text('words', sort=False)
    # Compare each recognized word to the closest occurrence of that word.
    for word in expected:
        target = fitz.Rect(word['x']*page.rect.width, word['y']*page.rect.height,
                           (word['x']+word['width'])*page.rect.width,
                           (word['y']+word['height'])*page.rect.height)
        candidates = [fitz.Rect(w[:4]) for w in actual if w[4] == word['text']]
        if not candidates:
            raise AssertionError(f"Missing selectable word {word['text']!r} on page {page.number+1}")
        error = min(max(abs(a-b) for a,b in zip(candidate,target)) for candidate in candidates)
        largest = max(largest,error)
        assert error < 1.5, (page.number+1, word['text'],error,target)
        checked += 1
    for term in ('En este caso hipotético,', 'mapas conceptuales'):
        for rect in page.search_for(term):
            print(f'Page {page.number+1}: {term!r} at {rect}')
    if page.number == 0:
        line = next(line for line in capture['lines'] if line['text'].startswith('En este caso'))
        words = line['words']
        for ending in ('conceptuales','representan','misma'):
            last = next(i for i,w in enumerate(words) if w['text'] == ending)
            prefix = words[:last+1]
            selected = fitz.Rect(min(w['x'] for w in prefix)*page.rect.width,
                                 min(w['y'] for w in prefix)*page.rect.height - 0.5,
                                 max(w['x']+w['width'] for w in prefix)*page.rect.width + 0.5,
                                 max(w['y']+w['height'] for w in prefix)*page.rect.height + 0.5)
            actual_text = ' '.join(page.get_textbox(selected).split())
            expected_text = ' '.join(w['text'] for w in prefix)
            assert actual_text == expected_text, (ending,actual_text,expected_text)
            print(f'PASS: visual selection ending at {ending!r} selects exactly {actual_text!r}')
            if ending == 'representan':
                for w in prefix:
                    candidates = [fitz.Rect(a[:4]) for a in actual if a[4] == w['text']]
                    target_x, target_y = w['x']*page.rect.width,w['y']*page.rect.height
                    rect = min(candidates,key=lambda r:abs(r.x0-target_x)+abs(r.y0-target_y))
                    page.add_highlight_annot(rect)
        page.get_pixmap(matrix=fitz.Matrix(1.6,1.6),clip=fitz.Rect(50,65,page.rect.width-40,210)).save(Path(sys.argv[1]).with_suffix('.alignment.png'))
print(f'PASS: {checked} words match OCR bounds; maximum error {largest:.3f} PDF points; {len(pdf)} pages.')
