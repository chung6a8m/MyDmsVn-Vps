# VPS-08 root-owned site template. Replace every __PLACEHOLDER__, verify the domain,
# certificate chain and permissions, then install under /etc/nginx/sites-available/.
# proxy-token.conf MUST be root:root 0600 and contain exactly:
# proxy_set_header X-MyDmsVn-Proxy-Token "AN_APPROVED_RANDOM_TOKEN";
# Never commit the token or expose it via nginx -T logs/artifacts.
server {
    listen 80;
    server_name __DOMAIN__;
    return 301 https://__DOMAIN__$request_uri;
}
server {
    listen 443 ssl;
    server_name __DOMAIN__;

    ssl_certificate __FULLCHAIN_PEM__;
    ssl_certificate_key __PRIVATEKEY_PEM__;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_session_tickets off;

    client_max_body_size 1m;
    client_body_timeout 20s;
    send_timeout 30s;
    add_header Strict-Transport-Security "max-age=31536000" always;
    add_header X-Content-Type-Options "nosniff" always;

    location / {
        proxy_pass http://127.0.0.1:5080;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        proxy_set_header Host $host;
        # Replace, never append, client-supplied forwarded values.
        proxy_set_header X-Forwarded-For $remote_addr;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header Forwarded "";
        proxy_set_header X-Forwarded-Host "";
        proxy_set_header X-Forwarded-Port "";
        proxy_set_header X-Forwarded-Prefix "";
        proxy_set_header X-Real-IP "";
        proxy_set_header X-Original-For "";
        proxy_set_header X-Original-Proto "";
        include /etc/nginx/mydmsvn/proxy-token.conf;

        proxy_request_buffering on;
        proxy_buffering off;
        proxy_connect_timeout 5s;
        proxy_read_timeout 60s;
        proxy_send_timeout 30s;
    }
}
