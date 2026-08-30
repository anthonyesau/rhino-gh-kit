# @component
# {
#   "name":        "Audit Hash PY",
#   "nickname":    "AHash",
#   "description": "Adds two numbers. Tests the hash-comment header parser.",
#
#   "inputs": [
#     { "name": "A", "type": "double", "access": "item",
#       "description": "First addend." },
#     { "name": "B", "type": "double", "access": "item",
#       "description": "Second addend." }
#   ],
#
#   "outputs": [
#     { "name": "Sum", "type": "double", "access": "item",
#       "description": "The sum." }
#   ]
# }
Sum = (A or 0) + (B or 0)
