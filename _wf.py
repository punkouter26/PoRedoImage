import sys, json
d = json.loads(sys.stdin.read())
for w in d.get('workflows', []):
    print(w['id'], w['state'], w['name'], '->', w.get('path','?'))
