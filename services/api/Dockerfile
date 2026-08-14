FROM python:3.12-slim

WORKDIR /srv

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy all source into /srv/api/ — preserves package structure
COPY . ./api/

# Copy the published NoirWings.Vm engine (self-contained)
COPY noirwings-runtime/ /opt/noirwings/
RUN chmod +x /opt/noirwings/NoirWings.Vm

# Create work directory for obfuscation jobs
RUN mkdir -p /tmp/noirwings

EXPOSE 8000

ENV PORT=8000

CMD ["python", "-c", "import os, uvicorn; uvicorn.run('api.main:app', host='0.0.0.0', port=int(os.environ.get('PORT', '8000')))"]
